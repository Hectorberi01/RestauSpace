using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Workers;

/// <summary>
/// Le worker du module (§ 25, 26, 29, 30) : envoie les notifications arrivées à échéance (les plus urgentes d'abord),
/// reprend les tentatives bloquées, applique les webhooks reçus, et expurge les notifications terminées. Chaque
/// notification est traitée dans son propre scope, POUR son tenant (<see cref="BackgroundTenant"/>), ou sans tenant pour
/// la plateforme. Plusieurs instances peuvent tourner : une notification prise par l'une est refusée à l'autre (version
/// de ligne). Journal : ids et codes seulement.
/// </summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes, NotificationsOptions options, NotificationsSettings settings, IClock clock, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    private const int MaxWebhookAttempts = 10;
    private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InboxRetention = TimeSpan.FromDays(30);

    /// <summary>Un passage d'envoi ; renvoie le nombre de notifications traitées sans erreur.</summary>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NotificationLocator> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<INotificationDirectory>().ListDueAsync(clock.UtcNow, options.BatchSize, cancellationToken);
        }

        var succeeded = 0;
        foreach (var notification in due)
        {
            if (await RunForAsync(notification, services => services.GetRequiredService<ICommandHandler<DispatchNotificationCommand>>()
                .HandleAsync(new DispatchNotificationCommand(notification.Id.Value), cancellationToken), "dispatch"))
            {
                succeeded++;
            }
        }

        // Un lot entièrement en erreur ne relance pas aussitôt un nouveau passage (pas de boucle serrée).
        return succeeded;
    }

    /// <summary>
    /// Fait avancer les campagnes dues d'un lot chacune (les messages créés partent ensuite par la file habituelle) ;
    /// renvoie le nombre de campagnes qui ont encore des clients à traiter.
    /// </summary>
    public async Task<int> ProcessCampaignsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CampaignLocator> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<ICampaignDirectory>().ListDueAsync(clock.UtcNow, options.BatchSize, cancellationToken);
        }

        var unfinished = 0;
        foreach (var campaign in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(campaign.TenantId);
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessCampaignCommand, bool>>()
                    .HandleAsync(new ProcessCampaignCommand(campaign.Id.Value), cancellationToken))
                {
                    unfinished++;
                }
            }
            catch (ConcurrencyException)
            {
                // Une autre instance traite ce lot.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Campaign {CampaignId} batch failed: {Error}.", campaign.Id.Value,
                    exception is DomainException domain ? domain.Code : exception.GetType().Name);
            }
        }

        return unfinished;
    }

    /// <summary>Applique les webhooks en attente ; renvoie le nombre traité.</summary>
    public async Task<int> ApplyWebhooksAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var directory = scope.ServiceProvider.GetRequiredService<INotificationDirectory>();
        var now = clock.UtcNow;
        var pending = await context.Webhooks
            .Where(webhook => webhook.ProcessedAtUtc == null && webhook.NextAttemptAtUtc <= now)
            .OrderBy(webhook => webhook.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var webhook in pending)
        {
            webhook.Attempts++;
            if (webhook.Kind == WebhookEventKind.Ignored || webhook.ProviderMessageId is null)
            {
                webhook.ProcessedAtUtc = now;
                continue;
            }

            var located = await directory.FindByProviderMessageAsync(
                NotificationProviderCode.Create(webhook.Provider), ProviderMessageId.Create(webhook.ProviderMessageId), cancellationToken);
            if (located is null)
            {
                // Le webhook peut précéder l'enregistrement de l'envoi (id du message pas encore connu) : on réessaie.
                Postpone(webhook, now, "message_unknown");
                continue;
            }

            var applied = await RunForAsync(located, services => services.GetRequiredService<ICommandHandler<ApplyDeliveryReportCommand>>()
                .HandleAsync(
                    new ApplyDeliveryReportCommand(
                        located.Id.Value,
                        webhook.Provider,
                        webhook.ProviderMessageId,
                        webhook.Kind switch
                        {
                            WebhookEventKind.Delivered => DeliveryReportKind.Delivered,
                            WebhookEventKind.PartiallyDelivered => DeliveryReportKind.PartiallyDelivered,
                            _ => DeliveryReportKind.Failed,
                        },
                        webhook.FailureCode,
                        webhook.IsPermanentFailure),
                    cancellationToken), "webhook");
            if (applied)
            {
                webhook.ProcessedAtUtc = now;
                webhook.LastError = null;
            }
            else
            {
                Postpone(webhook, now, "apply_failed");
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    /// <summary>Tentatives bloquées, expurgation, ménage de l'inbox.</summary>
    public async Task HousekeepAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NotificationLocator> stuck;
        IReadOnlyList<NotificationLocator> toRedact;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<INotificationDirectory>();
            stuck = await directory.ListStuckAsync(clock.UtcNow - settings.ProcessingTimeout, options.BatchSize, cancellationToken);
            toRedact = await directory.ListToRedactAsync(clock.UtcNow - settings.RedactAfter, options.BatchSize, cancellationToken);
            var limit = clock.UtcNow - InboxRetention;
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Inbox
                .Where(message => message.ProcessedAtUtc < limit)
                .ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var notification in stuck)
        {
            await RunForAsync(notification, services => services.GetRequiredService<ICommandHandler<RecoverStuckNotificationCommand>>()
                .HandleAsync(new RecoverStuckNotificationCommand(notification.Id.Value), cancellationToken), "recover");
        }

        foreach (var notification in toRedact)
        {
            await RunForAsync(notification, services => services.GetRequiredService<ICommandHandler<RedactNotificationCommand>>()
                .HandleAsync(new RedactNotificationCommand(notification.Id.Value), cancellationToken), "redact");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.RunWorkers)
        {
            return;
        }

        var lastHousekeeping = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchDueAsync(stoppingToken) == options.BatchSize)
                {
                }

                await ApplyWebhooksAsync(stoppingToken);
                await ProcessCampaignsAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastHousekeeping > HousekeepingInterval)
                {
                    lastHousekeeping = DateTimeOffset.UtcNow;
                    await HousekeepAsync(stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Notifications worker pass failed; retrying.");
            }

            try
            {
                await Task.Delay(options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Exécute <paramref name="action"/> dans un scope qui agit pour le tenant de la notification.</summary>
    private async Task<bool> RunForAsync(NotificationLocator notification, Func<IServiceProvider, Task> action, string operation)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (notification.TenantId is { } tenant)
        {
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["NotificationId"] = notification.Id.Value,
            ["TenantId"] = notification.TenantId?.Value,
            ["Operation"] = operation,
        });
        try
        {
            await action(scope.ServiceProvider);
            return true;
        }
        catch (ConcurrencyException)
        {
            // Une autre instance l'a prise : rien à faire.
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Le type d'erreur et son code suffisent : jamais le message d'un prestataire ni une adresse.
            logger.LogWarning("Notification {NotificationId} {Operation} failed: {Error}.", notification.Id.Value, operation,
                exception is DomainException domain ? domain.Code : exception.GetType().Name);
            return false;
        }
    }

    private static void Postpone(NotificationWebhookEvent webhook, DateTimeOffset now, string error)
    {
        webhook.LastError = error;
        if (webhook.Attempts >= MaxWebhookAttempts)
        {
            webhook.ProcessedAtUtc = now;
            return;
        }

        webhook.NextAttemptAtUtc = now + TimeSpan.FromSeconds(Math.Min(3600, 15 * Math.Pow(2, webhook.Attempts)));
    }
}

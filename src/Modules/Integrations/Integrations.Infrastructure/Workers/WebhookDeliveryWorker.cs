using Integrations.Application;
using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Integrations.Infrastructure.Workers;

/// <summary>
/// Le job du module : clôt les tentatives perdues (« timeout »), envoie les livraisons dues, puis efface les livraisons
/// terminées anciennes. Un scope par livraison, POUR son tenant (<see cref="BackgroundTenant"/>). Deux instances de
/// l'hôte peuvent tourner : la tentative est enregistrée avant l'envoi, la seconde échoue sur la version. Journal : ids,
/// issues et compteurs ; jamais un corps, un en-tête ni un secret.
/// </summary>
public sealed class WebhookDeliveryWorker(
    IServiceScopeFactory scopes, IntegrationsOptions options, IntegrationsSettings settings, IClock clock, ILogger<WebhookDeliveryWorker> logger)
    : BackgroundService
{
    /// <summary>Un passage ; renvoie le nombre de tentatives faites (réussies ou non).</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DeliveryWork> stuck;
        IReadOnlyList<DeliveryWork> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IWebhookWorkQueue>();
            stuck = await queue.ListStuckAsync(clock.UtcNow - settings.StuckAttemptAfter, options.BatchSize, cancellationToken);
        }

        foreach (var work in stuck)
        {
            await ForTenantAsync(work, async services =>
            {
                if (await services.GetRequiredService<ICommandHandler<RecoverStuckDeliveryCommand, bool>>()
                    .HandleAsync(new RecoverStuckDeliveryCommand(work.DeliveryId.Value), cancellationToken))
                {
                    logger.LogWarning("Webhook delivery {DeliveryId}: lost attempt closed as timeout.", work.DeliveryId.Value);
                }
            });
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IWebhookWorkQueue>().ListDueAsync(clock.UtcNow, options.BatchSize, cancellationToken);
        }

        var attempts = 0;
        foreach (var work in due)
        {
            await ForTenantAsync(work, async services =>
            {
                var outcome = await services.GetRequiredService<ICommandHandler<AttemptWebhookDeliveryCommand, AttemptOutcome>>()
                    .HandleAsync(new AttemptWebhookDeliveryCommand(work.DeliveryId.Value), cancellationToken);
                if (outcome != AttemptOutcome.Nothing)
                {
                    attempts++;
                    logger.LogInformation("Webhook delivery {DeliveryId}: {Outcome}.", work.DeliveryId.Value, outcome);
                }
            });
        }

        return attempts;
    }

    /// <summary>Fin des périodes de grâce : les anciens secrets expirés sont oubliés et supprimés du coffre.</summary>
    public async Task<int> ExpirePreviousSecretsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EndpointWork> expired;
        await using (var scope = scopes.CreateAsyncScope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<IWebhookWorkQueue>()
                .ListExpiredPreviousSecretsAsync(clock.UtcNow, options.BatchSize, cancellationToken);
        }

        var count = 0;
        foreach (var work in expired)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(work.TenantId);
            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpirePreviousWebhookSecretCommand, bool>>()
                    .HandleAsync(new ExpirePreviousWebhookSecretCommand(work.EndpointId.Value), cancellationToken))
                {
                    count++;
                }
            }
            catch (Exception exception) when (exception is ConcurrencyException or DomainException or NotFoundException)
            {
                logger.LogWarning("Previous secret of webhook endpoint {EndpointId} not expired yet: {Error}.", work.EndpointId.Value, exception.GetType().Name);
            }
        }

        return count;
    }

    /// <summary>Efface un lot de livraisons terminées plus anciennes que la rétention ; renvoie leur nombre.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var purged = await scope.ServiceProvider.GetRequiredService<IWebhookWorkQueue>()
            .PurgeFinishedAsync(clock.UtcNow - options.DeliveryRetention, options.BatchSize * 10, cancellationToken);
        if (purged > 0)
        {
            logger.LogInformation("{Count} finished webhook deliveries purged.", purged);
        }

        return purged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var attempts = 0;
            try
            {
                attempts = await RunOnceAsync(stoppingToken);
                await ExpirePreviousSecretsAsync(stoppingToken);
                if (clock.UtcNow - lastPurge >= TimeSpan.FromHours(1))
                {
                    await PurgeAsync(stoppingToken);
                    lastPurge = clock.UtcNow;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Webhook delivery pass failed; retrying at the next interval.");
            }

            // Un lot plein : on enchaîne aussitôt ; sinon on attend.
            if (attempts >= options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ForTenantAsync(DeliveryWork work, Func<IServiceProvider, Task> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(work.TenantId);
        try
        {
            await action(scope.ServiceProvider);
        }
        catch (Exception exception) when (exception is ConcurrencyException or DomainException or NotFoundException)
        {
            // Une autre instance a pris la livraison, ou elle a changé entre-temps : rien à faire de plus.
            logger.LogWarning("Webhook delivery {DeliveryId} skipped: {Error}.", work.DeliveryId.Value, exception.GetType().Name);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Application;
using Payments.Application.Commands;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Persistence.Configurations;

namespace Payments.Infrastructure.Webhooks;

/// <summary>
/// Applique les webhooks en file, dans l'ordre d'arrivée. Chaque webhook est traité dans son propre scope, POUR le
/// restaurant dont le compte marchand l'a signé (<see cref="BackgroundTenant"/>). Issue :
/// <list type="bullet">
/// <item>appliqué (ou rejeu sans effet) : <c>applied</c> ;</item>
/// <item>refus métier (contradiction, montant faux...) : <c>rejected:&lt;code&gt;</c>, conservé pour enquête, jamais retenté ;</item>
/// <item>paiement encore introuvable : retenté pendant <see cref="UnknownReferenceGrace"/> (le webhook peut arriver
///   avant la fin de l'enregistrement de la tentative), puis <c>rejected:payments.payment.not_found</c> ;</item>
/// <item>erreur technique ou conflit de concurrence : retenté plus tard, de plus en plus espacé.</item>
/// </list>
/// Plusieurs instances peuvent tourner : un lot est verrouillé par celle qui le lit (FOR UPDATE SKIP LOCKED).
/// </summary>
public sealed class PaymentWebhookProcessor(IServiceScopeFactory scopes, IClock clock, ILogger<PaymentWebhookProcessor> logger)
    : BackgroundService
{
    public static readonly TimeSpan UnknownReferenceGrace = TimeSpan.FromMinutes(10);
    private const int BatchSize = 50; // Recopié dans DueSql.
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    // Constante : schéma et table viennent du code, jamais d'une entrée externe ; la date est un paramètre.
    private const string DueSql =
        "SELECT * FROM \"" + DependencyInjection.Schema + "\"." + PaymentWebhookConfiguration.Table
        + " WHERE processed_at_utc IS NULL AND next_attempt_at_utc <= {0} ORDER BY id LIMIT 50 FOR UPDATE SKIP LOCKED";

    /// <summary>Traite un lot de webhooks dus et renvoie le nombre de webhooks examinés.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = clock.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var due = await context.Webhooks
            .FromSqlRaw(DueSql, now)
            .ToListAsync(cancellationToken);

        try
        {
            foreach (var webhook in due)
            {
                await ProcessAsync(webhook, cancellationToken);
            }
        }
        finally
        {
            // Conserver les issues déjà obtenues même si le traitement s'est interrompu plus loin.
            await context.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        return due.Count;
    }

    private async Task ProcessAsync(PaymentWebhook webhook, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<BackgroundTenant>().ActFor(webhook.TenantId);
            await services.GetRequiredService<ICommandHandler<ApplyProviderResultCommand>>().HandleAsync(webhook.ToCommand(), cancellationToken);
            webhook.Complete("applied", now);
        }
        catch (NotFoundException) when (now - webhook.ReceivedAtUtc < UnknownReferenceGrace)
        {
            // Le webhook peut arriver avant la fin de l'enregistrement de la tentative : on attend un peu.
            webhook.RetryLater("Payment not found yet.", now);
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException)
        {
            var code = exception is DomainException domain ? domain.Code : ((NotFoundException)exception).Code;
            webhook.Complete("rejected:" + code, now);
            logger.LogWarning(
                exception, "Payment webhook {WebhookId} from {Provider} rejected ({Code}).", webhook.Id, webhook.Provider, code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            webhook.RetryLater(exception.GetType().Name + ": " + exception.Message, now);
            logger.LogError(exception, "Payment webhook {WebhookId} from {Provider} failed; retrying later.", webhook.Id, webhook.Provider);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcessPendingAsync(stoppingToken) == BatchSize)
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Payment webhook processing failed; retrying.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

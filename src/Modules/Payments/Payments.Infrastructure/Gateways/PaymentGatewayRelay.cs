using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Transmet au prestataire, APRÈS la sauvegarde (par l'outbox), ce que le restaurant a décidé :
/// <list type="bullet">
/// <item><c>PaymentCancelled</c> avec une tentative encore ouverte : annulation de l'intention chez le prestataire ;</item>
/// <item><c>RefundRequested</c> d'un paiement en ligne : demande de remboursement (notre id de remboursement sert de clé
///   d'idempotence ; l'issue reviendra par webhook).</item>
/// </list>
/// Un paiement du comptoir n'a pas de prestataire : rien à transmettre. Un prestataire injoignable fait échouer le message,
/// que l'outbox retente ; un refus DÉFINITIF d'un remboursement l'enregistre comme échoué (le montant redevient
/// remboursable), un refus définitif d'annulation est seulement journalisé.
/// </summary>
internal sealed class PaymentGatewayRelay(IServiceScopeFactory scopes, ILogger<PaymentGatewayRelay> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != DependencyInjection.Schema || message.Type is not (nameof(PaymentCancelled) or nameof(RefundRequested)))
        {
            return;
        }

        using var content = JsonDocument.Parse(message.Content);
        var root = content.RootElement;
        string? closedReference = null;
        if (message.Type == nameof(PaymentCancelled))
        {
            // Aucune tentative ouverte au moment de l'annulation : rien à annuler chez le prestataire.
            closedReference = root.TryGetProperty(nameof(PaymentCancelled.ClosedAttemptProviderReference), out var closed)
                && closed.ValueKind == JsonValueKind.String
                    ? closed.GetString()
                    : null;
            if (closedReference is null)
            {
                return;
            }
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var paymentId = PaymentId.From(root.GetProperty(nameof(PaymentCancelled.PaymentId)).GetGuid());
        var directory = services.GetRequiredService<PaymentDirectory>();
        var payment = await directory.FindAsync(paymentId, cancellationToken);
        if (payment is null || await GatewayAsync(services, directory, payment, cancellationToken) is not { } gateway)
        {
            return;
        }

        if (closedReference is not null)
        {
            await CancelAsync(gateway, ProviderReference.Create(closedReference), message, cancellationToken);
            return;
        }

        var refundId = RefundId.From(root.GetProperty(nameof(RefundRequested.RefundId)).GetGuid());
        if (payment.Refunds.SingleOrDefault(candidate => candidate.Id == refundId) is not { Status: RefundStatus.Requested } refund
            || payment.Attempts.FirstOrDefault(attempt => attempt.Status == PaymentAttemptStatus.Succeeded || attempt.WasCapturedAfterCancellation)
                is not { } captured)
        {
            // Déjà réglé (message rejoué) ou rien d'encaissé chez ce prestataire : rien à demander.
            return;
        }

        try
        {
            await gateway.RequestRefundAsync(
                new GatewayRefundRequest(payment.TenantId, payment.Id, refundId, captured.ProviderReference, refund.Amount), cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.IsPermanent)
        {
            logger.LogWarning(exception, "Refund {RefundId} of payment {PaymentId} refused by {Provider}; recorded as failed.", refundId, payment.Id, payment.Provider);
            await using var failure = scopes.CreateAsyncScope();
            failure.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(payment.TenantId);
            await failure.ServiceProvider.GetRequiredService<ICommandHandler<ApplyProviderResultCommand>>().HandleAsync(
                new ApplyProviderResultCommand(
                    payment.Provider.Value, ProviderResultKind.RefundFailed, captured.ProviderReference.Value, RefundId: refundId.Value),
                cancellationToken);
        }
    }

    /// <summary>
    /// Le prestataire branché sur le compte marchand qui a encaissé (celui du restaurant, ou celui de la plateforme en
    /// mode plateforme) ; null pour un paiement du comptoir, ou si la configuration n'a plus d'identifiants ni
    /// d'intégration (rien ne peut alors être transmis).
    /// </summary>
    private async Task<IPaymentGateway?> GatewayAsync(
        IServiceProvider services, PaymentDirectory directory, Payment payment, CancellationToken cancellationToken)
    {
        var integrations = services.GetRequiredService<IPaymentIntegrations>();
        var configuration = await directory.FindConfigurationAsync(payment.TenantId, payment.Provider, cancellationToken);
        if (configuration is null
            || CredentialReferences.Of(configuration) is not { } reference
            || integrations.Find(payment.Provider) is null)
        {
            return null;
        }

        try
        {
            var secrets = await services.GetRequiredService<IPaymentSecretStore>().ReadAsync(reference, cancellationToken);
            if (secrets is null)
            {
                logger.LogError("No credentials stored at {SecretReference}; payment {PaymentId} not relayed.", reference.Value, payment.Id);
                return null;
            }

            return integrations.CreateGateway(payment.Provider, secrets);
        }
        catch (InvalidOperationException exception)
        {
            // Retenter ne servirait à rien (clé changée) et bloquerait l'outbox : à traiter à la main chez le prestataire.
            // Seule la référence est journalisée, jamais une valeur.
            logger.LogError(exception, "Credentials at {SecretReference} are unreadable; payment {PaymentId} not relayed.", reference.Value, payment.Id);
            return null;
        }
    }

    private async Task CancelAsync(IPaymentGateway gateway, ProviderReference reference, OutboxEnvelope message, CancellationToken cancellationToken)
    {
        try
        {
            await gateway.CancelAsync(reference, cancellationToken);
        }
        catch (PaymentGatewayException exception) when (exception.IsPermanent)
        {
            // L'intention a pu être payée entre-temps : la capture tardive, si elle arrive, sera enregistrée et remboursable.
            logger.LogWarning(exception, "Cancellation of {Reference} refused by {Provider} (outbox message {MessageId}).", reference.Value, gateway.Provider, message.Id);
        }
    }
}

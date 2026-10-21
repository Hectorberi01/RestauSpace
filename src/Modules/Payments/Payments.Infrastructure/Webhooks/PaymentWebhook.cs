using Payments.Application.Commands;

namespace Payments.Infrastructure.Webhooks;

/// <summary>
/// Un webhook reçu d'un prestataire, signature vérifiée, conservé tel quel (corps brut) avec sa lecture normalisée.
/// Unique par (configuration de paiement du restaurant, id d'événement) : un webhook renvoyé n'est enregistré qu'une fois. Traité en arrière-plan
/// par <see cref="PaymentWebhookProcessor"/>. Pas un agrégat métier : une file d'entrée de l'Infrastructure.
/// </summary>
public sealed class PaymentWebhook
{
    public const int MaxEventIdLength = 255;
    public const int MaxOutcomeLength = 200;
    public const int MaxErrorLength = 2000;

    private PaymentWebhook()
    {
        Provider = null!;
        EventId = null!;
        RawBody = null!;
        Reference = null!;
    }

    public PaymentWebhook(
        TenantId tenantId, Guid configurationId, string provider, VerifiedWebhook webhook, string rawBody, DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        TenantId = tenantId;
        ConfigurationId = configurationId;
        Provider = provider;
        EventId = webhook.EventId;
        RawBody = rawBody;
        Kind = webhook.Kind;
        Reference = webhook.Reference;
        Amount = webhook.Amount;
        CurrencyCode = webhook.CurrencyCode;
        RefundId = webhook.RefundId;
        Reason = webhook.Reason;
        ReceivedAtUtc = receivedAtUtc;
        NextAttemptAtUtc = receivedAtUtc;
    }

    public long Id { get; private set; }

    /// <summary>Le restaurant dont le compte marchand a signé ce webhook (connu par l'adresse appelée, prouvé par la signature).</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>La configuration de paiement du restaurant dont l'adresse a été appelée.</summary>
    public Guid ConfigurationId { get; private set; }

    public string Provider { get; private set; }

    public string EventId { get; private set; }

    public string RawBody { get; private set; }

    public ProviderResultKind Kind { get; private set; }

    public string Reference { get; private set; }

    public decimal? Amount { get; private set; }

    public string? CurrencyCode { get; private set; }

    public Guid? RefundId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    /// <summary><c>applied</c>, ou <c>rejected:&lt;code&gt;</c> quand le résultat contredit ce qui est enregistré.</summary>
    public string? Outcome { get; private set; }

    public ApplyProviderResultCommand ToCommand() =>
        new(Provider, Kind, Reference, Amount, CurrencyCode, RefundId, Reason);

    public void Complete(string outcome, DateTimeOffset nowUtc)
    {
        Outcome = outcome.Length > MaxOutcomeLength ? outcome[..MaxOutcomeLength] : outcome;
        ProcessedAtUtc = nowUtc;
    }

    /// <summary>Échec passager : nouvel essai plus tard, de plus en plus espacé (2 s, 4 s... jusqu'à 5 min).</summary>
    public void RetryLater(string error, DateTimeOffset nowUtc)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        NextAttemptAtUtc = nowUtc.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(Attempts, 9))));
    }
}

/// <summary>Lecture normalisée d'un webhook vérifié.</summary>
/// <param name="EventId">Id de l'événement chez le prestataire (dédoublonnage).</param>
/// <param name="Reference">Référence de la tentative visée (celle renvoyée à la création de l'intention).</param>
/// <param name="RefundId">Pour un résultat de remboursement : NOTRE id, transmis au prestataire à la demande.</param>
public sealed record VerifiedWebhook(
    string EventId,
    ProviderResultKind Kind,
    string Reference,
    decimal? Amount = null,
    string? CurrencyCode = null,
    Guid? RefundId = null,
    string? Reason = null);

public enum WebhookReadStatus
{
    /// <summary>Signature absente, fausse ou trop ancienne, ou corps illisible : refusé.</summary>
    Invalid = 1,

    /// <summary>Authentique mais sans intérêt pour nous (autre type d'événement) : accusé de réception, rien d'autre.</summary>
    Ignored = 2,

    Verified = 3,
}

public sealed record WebhookReadResult(WebhookReadStatus Status, VerifiedWebhook? Webhook = null)
{
    public static WebhookReadResult Invalid { get; } = new(WebhookReadStatus.Invalid);

    public static WebhookReadResult Ignored { get; } = new(WebhookReadStatus.Ignored);

    public static WebhookReadResult Verified(VerifiedWebhook webhook) => new(WebhookReadStatus.Verified, webhook);
}

/// <summary>
/// Authentifie un webhook d'UN prestataire et le traduit en <see cref="VerifiedWebhook"/>. Une implémentation par
/// prestataire en ligne, à côté de son <c>IPaymentGateway</c>. Selon le prestataire, l'authenticité vient de la signature,
/// ou de la relecture de la transaction chez lui avec les identifiants du restaurant (le corps reçu ne sert alors qu'à
/// savoir quoi relire) : d'où la lecture asynchrone.
/// </summary>
public interface IPaymentWebhookReader
{
    /// <summary>Code du prestataire, tel qu'il apparaît dans l'URL du webhook.</summary>
    string Provider { get; }

    /// <param name="headers">En-têtes de la requête, noms insensibles à la casse.</param>
    /// <exception cref="Application.Abstractions.PaymentGatewayException">Prestataire injoignable pendant la relecture.</exception>
    Task<WebhookReadResult> ReadAsync(
        IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

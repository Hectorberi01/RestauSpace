namespace Payments.Domain;

/// <summary>
/// Remboursement d'une partie (ou de la totalité) du montant capturé. Tant qu'il est Requested, son montant est
/// réservé sur le paiement ; Succeeded et Failed sont définitifs (une seconde issue différente est refusée : pas de
/// double traitement). <see cref="ProviderReference"/> : la référence du remboursement chez le prestataire, quand il en
/// donne une (unique dans le paiement).
/// </summary>
public sealed class Refund : Entity<RefundId>
{
    public const int MaxReasonLength = 500;

    private Refund(RefundId id, Money amount, string reason, DateTimeOffset requestedAtUtc)
        : base(id)
    {
        Amount = amount;
        Reason = reason;
        Status = RefundStatus.Requested;
        RequestedAtUtc = requestedAtUtc;
    }

    // Réservé à l'ORM.
    private Refund()
    {
        Amount = null!;
        Reason = null!;
    }

    public Money Amount { get; private set; }

    public string Reason { get; private set; }

    public RefundStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public ProviderReference? ProviderReference { get; private set; }

    /// <summary>Instant où le prestataire a donné son issue définitive (réussi ou échoué).</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    internal static Refund Request(Money amount, string? reason, DateTimeOffset requestedAtUtc)
    {
        DomainException.ThrowIf(!amount.IsPositive, PaymentErrors.RefundAmountNotPositive);
        var trimmed = reason?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxReasonLength, PaymentErrors.RefundReasonInvalid);
        return new Refund(RefundId.New(), amount, trimmed!, requestedAtUtc);
    }

    internal void Succeed(DateTimeOffset nowUtc, ProviderReference? providerReference = null) =>
        Close(RefundStatus.Succeeded, nowUtc, providerReference);

    internal void Fail(DateTimeOffset nowUtc, ProviderReference? providerReference = null) =>
        Close(RefundStatus.Failed, nowUtc, providerReference);

    /// <summary>Vrai si cette issue rejouée est compatible avec celle enregistrée (même statut, pas d'autre référence).</summary>
    internal bool IsSameOutcome(RefundStatus finalStatus, ProviderReference? providerReference) =>
        Status == finalStatus && (providerReference is null || ProviderReference is null || ProviderReference == providerReference);

    private void Close(RefundStatus finalStatus, DateTimeOffset nowUtc, ProviderReference? providerReference)
    {
        DomainException.ThrowIf(Status != RefundStatus.Requested, PaymentErrors.RefundOutcomeConflict);
        Status = finalStatus;
        ProviderReference = providerReference;
        CompletedAtUtc = nowUtc;
    }
}

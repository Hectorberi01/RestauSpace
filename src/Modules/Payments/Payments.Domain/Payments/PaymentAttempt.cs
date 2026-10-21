namespace Payments.Domain;

/// <summary>
/// Une tentative chez le prestataire, identifiée par sa <see cref="ProviderReference"/>. Pending jusqu'à la réponse du
/// prestataire ; Succeeded (capturée) et Failed sont définitifs. Une autorisation la laisse Pending : l'argent est
/// bloqué, pas prélevé. Une tentative terminée n'est jamais rouverte. Aucune donnée bancaire n'y est conservée : la
/// référence du prestataire, un code d'échec et, au plus, son message borné.
/// </summary>
public sealed class PaymentAttempt : Entity<PaymentAttemptId>
{
    public const int MaxFailureReasonLength = 500;

    private PaymentAttempt(PaymentAttemptId id, ProviderReference providerReference, DateTimeOffset startedAtUtc)
        : base(id)
    {
        ProviderReference = providerReference;
        Status = PaymentAttemptStatus.Pending;
        StartedAtUtc = startedAtUtc;
    }

    // Réservé à l'ORM.
    private PaymentAttempt()
    {
        ProviderReference = null!;
    }

    public ProviderReference ProviderReference { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? AuthorizedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    /// <summary>Code d'échec normalisé ; null tant que la tentative n'a pas échoué.</summary>
    public PaymentFailureCode? FailureCode { get; private set; }

    /// <summary>Message du prestataire, facultatif et borné (jamais une donnée bancaire).</summary>
    public string? FailureReason { get; private set; }
    public bool IsOpen => Status == PaymentAttemptStatus.Pending;
    public bool IsAuthorized => AuthorizedAtUtc is not null;
    /// <summary>Renseigné si le prestataire a prélevé l'argent malgré l'annulation du paiement. La tentative reste Failed.</summary>
    public DateTimeOffset? CapturedAfterCancellationAtUtc { get; private set; }
    public bool WasCapturedAfterCancellation => CapturedAfterCancellationAtUtc is not null;
    internal static PaymentAttempt Create(ProviderReference providerReference, DateTimeOffset startedAtUtc) =>
        new(PaymentAttemptId.New(), providerReference, startedAtUtc);

    internal void MarkAuthorized(DateTimeOffset nowUtc)
    {
        EnsureOpen();
        AuthorizedAtUtc ??= nowUtc;
    }

    internal void Succeed(DateTimeOffset nowUtc)
    {
        EnsureOpen();
        Status = PaymentAttemptStatus.Succeeded;
        CompletedAtUtc = nowUtc;
    }

    internal void Fail(PaymentFailureCode failureCode, string? reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(failureCode);
        EnsureOpen();
        Status = PaymentAttemptStatus.Failed;
        FailureCode = failureCode;
        FailureReason = reason;
        CompletedAtUtc = nowUtc;
    }

    internal void MarkCapturedAfterCancellation(DateTimeOffset nowUtc)
    {
        DomainException.ThrowIf(Status == PaymentAttemptStatus.Succeeded, PaymentErrors.AttemptAlreadySucceeded);
        DomainException.ThrowIf(Status != PaymentAttemptStatus.Failed, PaymentErrors.LateCaptureNotAllowed);
        CapturedAfterCancellationAtUtc ??= nowUtc;
    }

    private void EnsureOpen()
    {
        DomainException.ThrowIf(Status == PaymentAttemptStatus.Succeeded, PaymentErrors.AttemptAlreadySucceeded);
        DomainException.ThrowIf(Status == PaymentAttemptStatus.Failed, PaymentErrors.AttemptAlreadyFailed);
    }
}

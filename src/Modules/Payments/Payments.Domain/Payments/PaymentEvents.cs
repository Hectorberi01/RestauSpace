namespace Payments.Domain;

public sealed record PaymentCreated(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    LocationId LocationId,
    Money Amount,
    PaymentProviderCode Provider,
    PaymentMethodType Method,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentAttemptRegistered(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentAttemptId AttemptId,
    ProviderReference ProviderReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentAuthorized(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentAttemptId AttemptId,
    ProviderReference ProviderReference,
    Money Amount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentCaptured(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentAttemptId AttemptId,
    ProviderReference ProviderReference,
    Money CapturedAmount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentSettlementChanged(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    Money SettledAmount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Ce que les paiements d'un acompte pas encore déduit détiennent pour leur réservation (dérivé à la sauvegarde).</summary>
public sealed record DepositSettlementChanged(
    TenantId TenantId,
    PaymentId PaymentId,
    Guid ReservationId,
    Money SettledAmount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentFailed(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentAttemptId AttemptId,
    ProviderReference ProviderReference,
    PaymentFailureCode FailureCode,
    string? Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// <paramref name="ClosedAttemptId"/> et <paramref name="ClosedAttemptProviderReference"/> désignent la tentative encore
/// ouverte (en attente ou autorisée) que l'annulation a close, pour que l'autorisation puisse être annulée chez
/// le prestataire. Les deux sont null quand aucune tentative n'était ouverte.
/// </summary>
public sealed record PaymentCancelled(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    string Reason,
    PaymentAttemptId? ClosedAttemptId,
    ProviderReference? ClosedAttemptProviderReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Le prestataire a prélevé l'argent pour une tentative d'un paiement qui avait été annulé. Volontairement pas un
/// <see cref="PaymentCaptured"/> : la commande n'en est pas payée, le montant est à rembourser.
/// </summary>
public sealed record PaymentCapturedAfterCancellation(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    PaymentAttemptId AttemptId,
    ProviderReference ProviderReference,
    Money CapturedAmount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Un remboursement a été demandé et son montant est désormais réservé ; aucun argent n'a encore bougé.</summary>
public sealed record RefundRequested(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    RefundId RefundId,
    Money Amount,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// La réponse définitive du prestataire pour un remboursement a été enregistrée sur le paiement, quelle qu'elle soit
/// (voir <see cref="Outcome"/>). <see cref="PaymentStatus"/> est le statut du paiement après cet enregistrement.
/// </summary>
public sealed record RefundRegistered(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    RefundId RefundId,
    Money Amount,
    RefundOutcome Outcome,
    PaymentStatus PaymentStatus,
    ProviderReference? RefundProviderReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Argent effectivement rendu au payeur. Levé en plus de <see cref="RefundRegistered"/>, en cas de succès seulement.</summary>
public sealed record RefundSucceeded(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    RefundId RefundId,
    Money Amount,
    Money TotalRefunded,
    bool IsFullRefund,
    ProviderReference? RefundProviderReference,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>L'acompte d'une réservation a été déduit d'une addition : le paiement règle désormais la commande.</summary>
public sealed record DepositApplied(
    TenantId TenantId,
    PaymentId PaymentId,
    Guid ReservationId,
    OrderId OrderId,
    Money Amount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

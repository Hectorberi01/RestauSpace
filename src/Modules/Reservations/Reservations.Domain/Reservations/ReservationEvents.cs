namespace Reservations.Domain;

public sealed record ReservationCreated(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    CustomerId? CustomerId,
    int PartySize,
    DateTimeOffset StartAtUtc,
    TimeSpan Duration,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationConfirmed(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationRescheduled(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    DateTimeOffset PreviousStartAtUtc,
    TimeSpan PreviousDuration,
    DateTimeOffset StartAtUtc,
    TimeSpan Duration,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationPartySizeChanged(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    int PreviousPartySize,
    int PartySize,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record GuestSeated(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    int PartySize,
    TableSessionId? TableSessionId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationCompleted(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationCancelled(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    ReservationActor CancelledBy,
    string? Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationNoShow(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    CustomerId? CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Ids uniquement : les coordonnées effacées ne doivent pas circuler dans l'événement.</summary>
public sealed record ReservationGuestAnonymized(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    CustomerId? CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La réservation a été rattachée à une autre fiche client, ou détachée (<paramref name="CustomerId"/> null).</summary>
public sealed record ReservationCustomerChanged(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    CustomerId? PreviousCustomerId,
    CustomerId? CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationDepositRequested(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    Money Amount,
    DateTimeOffset DueBeforeUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationDepositPaid(
    TenantId TenantId,
    ReservationId ReservationId,
    LocationId LocationId,
    Money Amount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ReservationDepositWaived(TenantId TenantId, ReservationId ReservationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ReservationDepositApplied(TenantId TenantId, ReservationId ReservationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>L'acompte payé doit être rendu au client : l'hôte demande le remboursement à Payments.</summary>
public sealed record ReservationDepositRefundRequested(TenantId TenantId, ReservationId ReservationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ReservationDepositRefunded(TenantId TenantId, ReservationId ReservationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Le restaurant garde l'acompte (no-show, annulation tardive décidée par l'équipe).</summary>
public sealed record ReservationDepositForfeited(TenantId TenantId, ReservationId ReservationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

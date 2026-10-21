namespace Floor.Domain;

/// <summary><paramref name="Reservation"/> est null pour un client sans réservation.</summary>
public sealed record TableSessionOpened(
    TenantId TenantId,
    TableSessionId TableSessionId,
    LocationId LocationId,
    TableId TableId,
    int GuestCount,
    ReservationReference? Reservation,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableSessionGuestCountChanged(
    TenantId TenantId,
    TableSessionId TableSessionId,
    LocationId LocationId,
    TableId TableId,
    int PreviousGuestCount,
    int GuestCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableSessionClosed(
    TenantId TenantId,
    TableSessionId TableSessionId,
    LocationId LocationId,
    TableId TableId,
    int GuestCount,
    ReservationReference? Reservation,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Les convives ont changé de table ; leurs commandes suivent la session.</summary>
public sealed record TableSessionMoved(
    TenantId TenantId,
    TableSessionId TableSessionId,
    LocationId LocationId,
    TableId PreviousTableId,
    TableId TableId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Deux tables réunies : la session <paramref name="TableSessionId"/> a rejoint <paramref name="IntoTableSessionId"/>.</summary>
public sealed record TableSessionMerged(
    TenantId TenantId,
    TableSessionId TableSessionId,
    TableSessionId IntoTableSessionId,
    LocationId LocationId,
    TableId TableId,
    TableId IntoTableId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

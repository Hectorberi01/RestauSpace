namespace Locations.Domain.Locations;

public sealed record RestaurantLocationCreated(
    TenantId TenantId,
    LocationId LocationId,
    string Name,
    string CountryCode,
    int Capacity,
    string TimeZoneId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationActivated(TenantId TenantId, LocationId LocationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationTemporarilyClosed(
    TenantId TenantId,
    LocationId LocationId,
    string? Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationReopened(TenantId TenantId, LocationId LocationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationArchived(
    TenantId TenantId,
    LocationId LocationId,
    LocationStatus PreviousStatus,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationRenamed(
    TenantId TenantId,
    LocationId LocationId,
    string PreviousName,
    string NewName,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationAddressChanged(
    TenantId TenantId,
    LocationId LocationId,
    Address Address,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Les coordonnées ont changé ; leurs valeurs ne voyagent pas dans l'événement.</summary>
public sealed record LocationContactChanged(TenantId TenantId, LocationId LocationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BusinessHoursChanged(
    TenantId TenantId,
    LocationId LocationId,
    BusinessHours Hours,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationCapacityChanged(
    TenantId TenantId,
    LocationId LocationId,
    int PreviousCapacity,
    int NewCapacity,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LocationTimeZoneChanged(
    TenantId TenantId,
    LocationId LocationId,
    string PreviousTimeZoneId,
    string NewTimeZoneId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ExceptionalScheduleAdded(
    TenantId TenantId,
    LocationId LocationId,
    ExceptionalScheduleId ExceptionalScheduleId,
    DateOnly Date,
    bool IsClosed,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ExceptionalScheduleRemoved(
    TenantId TenantId,
    LocationId LocationId,
    ExceptionalScheduleId ExceptionalScheduleId,
    DateOnly Date,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

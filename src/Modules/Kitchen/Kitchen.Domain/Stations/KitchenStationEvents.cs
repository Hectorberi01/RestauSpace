namespace Kitchen.Domain;

public sealed record KitchenStationCreated(TenantId TenantId, StationId StationId, LocationId LocationId, string Name, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record KitchenStationActivated(TenantId TenantId, StationId StationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record KitchenStationDeactivated(TenantId TenantId, StationId StationId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

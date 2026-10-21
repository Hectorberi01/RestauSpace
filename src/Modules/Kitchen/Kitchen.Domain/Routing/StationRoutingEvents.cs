namespace Kitchen.Domain;

public sealed record StationRoutingCreated(
    TenantId TenantId,
    StationRoutingId RoutingId,
    LocationId LocationId,
    StationId DefaultStationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StationRoutingDefaultStationChanged(
    TenantId TenantId,
    StationRoutingId RoutingId,
    LocationId LocationId,
    StationId PreviousStationId,
    StationId NewStationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary><see cref="PreviousStationId"/> est null quand le produit n'avait pas encore de routage.</summary>
public sealed record StationRouteSet(
    TenantId TenantId,
    StationRoutingId RoutingId,
    LocationId LocationId,
    ProductId ProductId,
    StationId StationId,
    StationId? PreviousStationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StationRouteRemoved(
    TenantId TenantId,
    StationRoutingId RoutingId,
    LocationId LocationId,
    ProductId ProductId,
    StationId StationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Le service d'un produit ; <see cref="KitchenCourse.Direct"/> l'efface.</summary>
public sealed record ProductCourseSet(
    TenantId TenantId,
    StationRoutingId RoutingId,
    LocationId LocationId,
    ProductId ProductId,
    KitchenCourse Course,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

namespace Floor.Domain;

public sealed record DiningAreaCreated(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    string Name,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DiningAreaRenamed(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    string PreviousName,
    string Name,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableAdded(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    string Label,
    int Capacity,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableRemoved(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableCapacityChanged(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    int PreviousCapacity,
    int Capacity,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableSetOutOfService(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableRestored(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TableRenamed(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    TableId TableId,
    string PreviousLabel,
    string Label,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TablePlaced(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    TableId TableId,
    int X,
    int Y,
    int Width,
    int Height,
    TableShape Shape,
    int Rotation,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La salle et toutes ses tables sont supprimées.</summary>
public sealed record DiningAreaDeleted(
    TenantId TenantId,
    DiningAreaId DiningAreaId,
    LocationId LocationId,
    IReadOnlyList<TableId> TableIds,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

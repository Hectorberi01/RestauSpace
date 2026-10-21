namespace Catalog.Domain;

public sealed record MenuPublished(TenantId TenantId, MenuId MenuId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record MenuUnpublished(TenantId TenantId, MenuId MenuId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record MenuArchived(TenantId TenantId, MenuId MenuId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Les créneaux du menu ont changé ; aucun = proposé à toute heure d'ouverture.</summary>
public sealed record MenuScheduleChanged(TenantId TenantId, MenuId MenuId, LocationId LocationId, int SlotCount, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

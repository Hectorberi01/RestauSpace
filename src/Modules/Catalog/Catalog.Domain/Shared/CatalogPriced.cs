namespace Catalog.Domain;

public sealed record CatalogPriced(TenantId TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

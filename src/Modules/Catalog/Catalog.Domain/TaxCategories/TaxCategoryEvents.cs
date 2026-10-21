namespace Catalog.Domain;

public sealed record TaxCategoryCreated(TenantId TenantId, TaxCategoryId TaxCategoryId, string Code, string Name, decimal Rate, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TaxCategoryRateChanged(
    TenantId TenantId,
    TaxCategoryId TaxCategoryId,
    string Code,
    decimal PreviousRate,
    decimal NewRate,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TaxCategoryArchived(TenantId TenantId, TaxCategoryId TaxCategoryId, string Code, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

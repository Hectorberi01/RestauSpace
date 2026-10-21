namespace Promotions.Domain;

public sealed record PromotionCreated(TenantId TenantId, PromotionId PromotionId, string Name, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PromotionActivated(TenantId TenantId, PromotionId PromotionId, Period ValidityPeriod, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PromotionPaused(TenantId TenantId, PromotionId PromotionId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PromotionResumed(TenantId TenantId, PromotionId PromotionId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PromotionRulesChanged(TenantId TenantId, PromotionId PromotionId, PromotionRules Rules, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PromotionApplied(
    TenantId TenantId,
    PromotionId PromotionId,
    OrderId OrderId,
    Money Discount,
    int UsageCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PromotionUsageReverted(
    TenantId TenantId,
    PromotionId PromotionId,
    OrderId OrderId,
    int UsageCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PromotionExpired(TenantId TenantId, PromotionId PromotionId, int UsageCount, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

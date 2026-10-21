namespace Promotions.Domain;

public enum CouponStatus
{
    Active = 1,
    Disabled = 2,
}

public sealed record CouponCreated(
    TenantId TenantId,
    CouponId CouponId,
    PromotionId PromotionId,
    CouponCode Code,
    int MaxUses,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CouponRedeemed(
    TenantId TenantId,
    CouponId CouponId,
    PromotionId PromotionId,
    CouponCode Code,
    OrderId OrderId,
    int UsageCount,
    int RemainingUses,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CouponDisabled(TenantId TenantId, CouponId CouponId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record CouponReleased(
    TenantId TenantId,
    CouponId CouponId,
    PromotionId PromotionId,
    CouponCode Code,
    OrderId OrderId,
    int UsageCount,
    int RemainingUses,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

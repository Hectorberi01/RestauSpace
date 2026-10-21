using Promotions.Domain;

namespace Promotions.Infrastructure.Persistence;

/// <summary>
/// Une ligne du registre d'usages : la promotion <see cref="PromotionId"/> a accordé <see cref="Discount"/> à la
/// commande <see cref="OrderId"/>. Écrite par l'unit of work à partir de <see cref="PromotionApplied"/>, dans la même
/// transaction que le compteur de la promotion ; jamais supprimée : <see cref="PromotionUsageReverted"/> la marque
/// rendue (<see cref="RevertedAtUtc"/>), ce qui garde la trace de toute remise accordée puis annulée.
/// L'index unique sur (promotion, commande) parmi les lignes NON rendues est la garde finale contre le double comptage
/// de deux transactions concurrentes.
/// </summary>
internal sealed class PromotionUsage : ITenantOwned
{
    // Réservé à l'ORM.
    private PromotionUsage()
    {
        Discount = null!;
    }

    public PromotionUsage(TenantId tenantId, PromotionId promotionId, OrderId orderId, Money discount, DateTimeOffset appliedAtUtc)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        PromotionId = promotionId;
        OrderId = orderId;
        Discount = discount;
        AppliedAtUtc = appliedAtUtc;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public PromotionId PromotionId { get; private set; }

    public OrderId OrderId { get; private set; }

    public Money Discount { get; private set; }

    public DateTimeOffset AppliedAtUtc { get; private set; }

    public DateTimeOffset? RevertedAtUtc { get; private set; }

    public bool IsHeld => RevertedAtUtc is null;

    public void Revert(DateTimeOffset revertedAtUtc) => RevertedAtUtc = revertedAtUtc;
}

/// <summary>
/// Une ligne du registre d'usages d'un coupon : le coupon <see cref="CouponId"/> a été utilisé par la commande
/// <see cref="OrderId"/>. Même cycle que <see cref="PromotionUsage"/> (<see cref="CouponRedeemed"/> l'écrit,
/// <see cref="CouponReleased"/> la marque rendue). Le code n'est pas recopié : il est sur le coupon.
/// </summary>
internal sealed class CouponUsage : ITenantOwned
{
    // Réservé à l'ORM.
    private CouponUsage()
    {
    }

    public CouponUsage(TenantId tenantId, CouponId couponId, PromotionId promotionId, OrderId orderId, DateTimeOffset redeemedAtUtc)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CouponId = couponId;
        PromotionId = promotionId;
        OrderId = orderId;
        RedeemedAtUtc = redeemedAtUtc;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CouponId CouponId { get; private set; }

    public PromotionId PromotionId { get; private set; }

    public OrderId OrderId { get; private set; }

    public DateTimeOffset RedeemedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public bool IsHeld => ReleasedAtUtc is null;

    public void Release(DateTimeOffset releasedAtUtc) => ReleasedAtUtc = releasedAtUtc;
}

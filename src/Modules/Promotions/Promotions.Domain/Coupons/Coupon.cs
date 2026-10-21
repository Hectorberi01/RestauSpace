namespace Promotions.Domain;

public sealed class Coupon : AggregateRoot<CouponId>, ITenantOwned
{
    private Coupon(CouponId id, TenantId tenantId, CouponCode code, PromotionId promotionId, int maxUses, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Code = code;
        PromotionId = promotionId;
        MaxUses = maxUses;
        Status = CouponStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Coupon() => Code = null!;

    public TenantId TenantId { get; }

    public CouponCode Code { get; private set; }

    public PromotionId PromotionId { get; private set; }

    public int MaxUses { get; private set; }

    public int UsageCount { get; private set; }

    public CouponStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public int RemainingUses => MaxUses - UsageCount;

    public bool IsExhausted => UsageCount >= MaxUses;

    public static Coupon Create(
        TenantId tenantId,
        PromotionReference promotion,
        CouponCode code,
        int maxUses,
        bool isCodeUnique,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(maxUses <= 0, CouponErrors.MaxUsesNotPositive);

        var now = clock.UtcNow;
        var coupon = new Coupon(CouponId.New(), tenantId, code, promotion.PromotionId, maxUses, now);
        coupon.EnsureSameTenant(promotion.TenantId);
        DomainException.ThrowIf(promotion.IsExpired, CouponErrors.PromotionExpired);
        DomainException.ThrowIf(!isCodeUnique, CouponErrors.CodeNotUnique);

        coupon.Raise(new CouponCreated(tenantId, coupon.Id, coupon.PromotionId, code, maxUses, now));
        return coupon;
    }

    /// <summary>
    /// Compte une utilisation pour une commande. <paramref name="promotion"/> est la promotion avec laquelle la
    /// remise est appliquée : ce doit être celle du coupon. <paramref name="orderAlreadyCounted"/> vient de
    /// <see cref="IPromotionUsageLedger"/>.
    /// </summary>
    public void Redeem(PromotionReference promotion, OrderId orderId, bool orderAlreadyCounted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        ArgumentNullException.ThrowIfNull(clock);
        // L'id de commande devient une ligne du registre d'utilisation : un id vide empoisonnerait la clé d'idempotence.
        Guard.NotEmpty(orderId);
        this.EnsureSameTenant(promotion.TenantId);
        DomainException.ThrowIf(promotion.PromotionId != PromotionId, CouponErrors.PromotionMismatch);
        DomainException.ThrowIf(orderAlreadyCounted, CouponErrors.AlreadyAppliedToOrder);
        DomainException.ThrowIf(promotion.IsExpired, CouponErrors.PromotionExpired);
        DomainException.ThrowIf(Status == CouponStatus.Disabled, CouponErrors.Disabled);
        DomainException.ThrowIf(IsExhausted, CouponErrors.Exhausted);

        UsageCount++;
        Raise(new CouponRedeemed(TenantId, Id, PromotionId, Code, orderId, UsageCount, RemainingUses, clock.UtcNow));
    }

    /// <summary>
    /// Restitue l'utilisation détenue par une commande annulée. Il s'agit de corriger un compteur, donc c'est aussi
    /// autorisé sur un coupon désactivé. <paramref name="orderHoldsUsage"/> vient de <see cref="IPromotionUsageLedger"/>.
    /// </summary>
    public void Release(OrderId orderId, bool orderHoldsUsage, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!orderHoldsUsage, CouponErrors.NoUsageForOrder);

        UsageCount = Math.Max(0, UsageCount - 1);
        Raise(new CouponReleased(TenantId, Id, PromotionId, Code, orderId, UsageCount, RemainingUses, clock.UtcNow));
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == CouponStatus.Disabled, CouponErrors.AlreadyDisabled);

        Status = CouponStatus.Disabled;
        Raise(new CouponDisabled(TenantId, Id, clock.UtcNow));
    }
}

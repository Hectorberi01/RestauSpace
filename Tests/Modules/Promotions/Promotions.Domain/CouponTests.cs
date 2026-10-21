using Promotions.Domain;

namespace Promotions.Domain;

public class CouponTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private readonly FakeClock _clock = new();
    private readonly Promotion _promotionAggregate;
    private readonly PromotionReference _promotion;

    public CouponTests()
    {
        _promotionAggregate = NewPromotion(Tenant);
        _promotion = _promotionAggregate.ToReference();
    }

    // Une PromotionReference ne se construit pas à la main : chaque test passe par une vraie promotion.
    private Promotion NewPromotion(TenantId tenant) =>
        Promotion.Create(
            tenant,
            "Welcome",
            PromotionRules.Create(Period.Create(FakeClock.DefaultNow.AddDays(-1), FakeClock.DefaultNow.AddDays(30))),
            _clock);

    private PromotionReference ExpiredPromotion()
    {
        _promotionAggregate.Expire(_clock);
        return _promotionAggregate.ToReference();
    }

    private Coupon NewCoupon(int maxUses = 3)
    {
        var coupon = Coupon.Create(Tenant, _promotion, CouponCode.Create("WELCOME10"), maxUses, isCodeUnique: true, _clock);
        coupon.ClearDomainEvents();
        return coupon;
    }

    [Theory]
    [InlineData("welcome10", "WELCOME10")]
    [InlineData("  Summer-2026 ", "SUMMER-2026")]
    [InlineData("abc", "ABC")]
    public void Code_is_trimmed_and_upper_cased(string input, string expected)
    {
        var code = CouponCode.Create(input);

        Assert.Equal(expected, code.Value);
        Assert.Equal(expected, code.ToString());
    }

    [Fact]
    public void Codes_differing_only_by_case_are_equal() =>
        Assert.Equal(CouponCode.Create("welcome"), CouponCode.Create("WELCOME"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Code_is_required(string? input) =>
        DomainAssert.Throws(CouponErrors.CodeRequired, () => CouponCode.Create(input));

    [Fact]
    public void Code_length_is_bounded()
    {
        DomainAssert.Throws(CouponErrors.CodeLengthInvalid, () => CouponCode.Create("AB"));
        DomainAssert.Throws(CouponErrors.CodeLengthInvalid, () => CouponCode.Create(new string('A', CouponCode.MaxLength + 1)));

        Assert.Equal(CouponCode.MaxLength, CouponCode.Create(new string('A', CouponCode.MaxLength)).Value.Length);
    }

    [Theory]
    [InlineData("WEL COME")]
    [InlineData("PROMO_10")]
    [InlineData("ÉTÉ2026")]
    [InlineData("50%OFF")]
    public void Code_rejects_characters_outside_the_charset(string input) =>
        DomainAssert.Throws(CouponErrors.CodeCharactersInvalid, () => CouponCode.Create(input));

    [Fact]
    public void Create_starts_active_and_unused()
    {
        var coupon = Coupon.Create(Tenant, _promotion, CouponCode.Create("welcome10"), 3, isCodeUnique: true, _clock);

        Assert.Equal(Tenant, coupon.TenantId);
        Assert.Equal(_promotion.PromotionId, coupon.PromotionId);
        Assert.Equal("WELCOME10", coupon.Code.Value);
        Assert.Equal(3, coupon.MaxUses);
        Assert.Equal(0, coupon.UsageCount);
        Assert.Equal(3, coupon.RemainingUses);
        Assert.False(coupon.IsExhausted);
        Assert.Equal(CouponStatus.Active, coupon.Status);
        Assert.Equal(FakeClock.DefaultNow, coupon.CreatedAtUtc);

        var created = DomainAssert.Raised<CouponCreated>(coupon);
        Assert.Equal(
            (Tenant, coupon.Id, _promotion.PromotionId, coupon.Code, 3),
            (created.TenantId, created.CouponId, created.PromotionId, created.Code, created.MaxUses));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Create_rejects_a_non_positive_max_uses(int maxUses) =>
        DomainAssert.Throws(
            CouponErrors.MaxUsesNotPositive,
            () => Coupon.Create(Tenant, _promotion, CouponCode.Create("WELCOME10"), maxUses, true, _clock));

    [Fact]
    public void Create_rejects_a_code_already_used_by_the_tenant() =>
        DomainAssert.Throws(
            CouponErrors.CodeNotUnique,
            () => Coupon.Create(Tenant, _promotion, CouponCode.Create("WELCOME10"), 3, isCodeUnique: false, _clock));

    [Fact]
    public void Create_rejects_a_promotion_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Coupon.Create(TenantId.New(), _promotion, CouponCode.Create("WELCOME10"), 3, true, _clock));

    [Fact]
    public void Create_rejects_an_expired_promotion() =>
        DomainAssert.Throws(
            CouponErrors.PromotionExpired,
            () => Coupon.Create(Tenant, ExpiredPromotion(), CouponCode.Create("WELCOME10"), 3, true, _clock));

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => Coupon.Create(default, _promotion, CouponCode.Create("WELCOME10"), 3, true, _clock));

    [Fact]
    public void Create_stamps_the_coupon_and_its_event_with_the_same_instant()
    {
        var coupon = Coupon.Create(Tenant, _promotion, CouponCode.Create("WELCOME10"), 3, true, new TickingClock());

        Assert.Equal(coupon.CreatedAtUtc, DomainAssert.Raised<CouponCreated>(coupon).OccurredAtUtc);
    }

    [Fact]
    public void Promotion_reference_cannot_be_built_outside_the_module()
    {
        Assert.DoesNotContain(typeof(PromotionReference).GetConstructors(), constructor => constructor.IsPublic);
        Assert.All(typeof(PromotionReference).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Redeem_rejects_an_empty_order_id()
    {
        var coupon = NewCoupon();

        DomainAssert.Throws(CommonErrors.EmptyId, () => coupon.Redeem(_promotion, default, false, _clock));

        Assert.Equal(0, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Redeem_counts_the_use_and_raises_CouponRedeemed()
    {
        var coupon = NewCoupon(maxUses: 3);
        var order = OrderId.New();

        coupon.Redeem(_promotion, order, false, _clock);

        Assert.Equal(1, coupon.UsageCount);
        Assert.Equal(2, coupon.RemainingUses);
        var redeemed = DomainAssert.Raised<CouponRedeemed>(coupon);
        Assert.Equal(
            (Tenant, coupon.Id, _promotion.PromotionId, coupon.Code, order, 1, 2, FakeClock.DefaultNow),
            (redeemed.TenantId, redeemed.CouponId, redeemed.PromotionId, redeemed.Code, redeemed.OrderId, redeemed.UsageCount, redeemed.RemainingUses, redeemed.OccurredAtUtc));
    }

    [Fact]
    public void Redeem_of_the_last_use_succeeds_once_then_is_rejected()
    {
        var coupon = NewCoupon(maxUses: 2);
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);
        coupon.ClearDomainEvents();

        // Deux requêtes se disputent la dernière utilisation : la première à atteindre l'agrégat l'emporte.
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);
        DomainAssert.Throws(CouponErrors.Exhausted, () => coupon.Redeem(_promotion, OrderId.New(), false, _clock));

        Assert.Equal(2, coupon.UsageCount);
        Assert.Equal(coupon.MaxUses, coupon.UsageCount);
        Assert.Equal(0, coupon.RemainingUses);
        Assert.True(coupon.IsExhausted);
        Assert.Equal(0, DomainAssert.Raised<CouponRedeemed>(coupon).RemainingUses);
    }

    [Fact]
    public void Usage_count_never_exceeds_max_uses()
    {
        var coupon = NewCoupon(maxUses: 3);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                coupon.Redeem(_promotion, OrderId.New(), false, _clock);
            }
            catch (DomainException)
            {
                // Attendu une fois épuisé.
            }
        }

        Assert.Equal(3, coupon.UsageCount);
        Assert.Equal(3, coupon.DomainEvents.OfType<CouponRedeemed>().Count());
    }

    [Fact]
    public void Redeem_rejects_a_disabled_coupon()
    {
        var coupon = NewCoupon();
        coupon.Disable(_clock);
        coupon.ClearDomainEvents();

        DomainAssert.Throws(CouponErrors.Disabled, () => coupon.Redeem(_promotion, OrderId.New(), false, _clock));

        Assert.Equal(0, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Disable_turns_the_coupon_off()
    {
        var coupon = NewCoupon();

        coupon.Disable(_clock);

        Assert.Equal(CouponStatus.Disabled, coupon.Status);
        var disabled = DomainAssert.Raised<CouponDisabled>(coupon);
        Assert.Equal((Tenant, coupon.Id), (disabled.TenantId, disabled.CouponId));
    }

    [Fact]
    public void Disable_rejects_an_already_disabled_coupon()
    {
        var coupon = NewCoupon();
        coupon.Disable(_clock);
        coupon.ClearDomainEvents();

        DomainAssert.Throws(CouponErrors.AlreadyDisabled, () => coupon.Disable(_clock));

        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Disable_is_allowed_on_an_exhausted_coupon()
    {
        var coupon = NewCoupon(maxUses: 1);
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);

        coupon.Disable(_clock);

        Assert.Equal(CouponStatus.Disabled, coupon.Status);
        DomainAssert.Throws(CouponErrors.Disabled, () => coupon.Redeem(_promotion, OrderId.New(), false, _clock));
    }

    [Fact]
    public void Redeem_rejects_a_promotion_of_another_tenant()
    {
        var coupon = NewCoupon();

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => coupon.Redeem(NewPromotion(TenantId.New()).ToReference(), OrderId.New(), false, _clock));

        Assert.Equal(0, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Redeem_rejects_another_promotion_than_its_own()
    {
        var coupon = NewCoupon();

        DomainAssert.Throws(
            CouponErrors.PromotionMismatch,
            () => coupon.Redeem(NewPromotion(Tenant).ToReference(), OrderId.New(), false, _clock));

        Assert.Equal(0, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Redeem_rejects_an_expired_promotion()
    {
        var coupon = NewCoupon();

        DomainAssert.Throws(
            CouponErrors.PromotionExpired,
            () => coupon.Redeem(ExpiredPromotion(), OrderId.New(), false, _clock));

        Assert.Equal(0, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Redeem_rejects_an_order_already_counted()
    {
        var coupon = NewCoupon();
        var order = OrderId.New();
        coupon.Redeem(_promotion, order, false, _clock);
        coupon.ClearDomainEvents();

        DomainAssert.Throws(CouponErrors.AlreadyAppliedToOrder, () => coupon.Redeem(_promotion, order, orderAlreadyCounted: true, _clock));

        Assert.Equal(1, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
        Assert.Equal("promotions.coupon.already_applied_to_order", CouponErrors.AlreadyAppliedToOrder.Code);
    }

    [Fact]
    public void Release_gives_the_use_back_and_raises_CouponReleased()
    {
        var coupon = NewCoupon(maxUses: 1);
        var order = OrderId.New();
        coupon.Redeem(_promotion, order, false, _clock);
        coupon.ClearDomainEvents();

        coupon.Release(order, orderHoldsUsage: true, _clock);

        Assert.Equal(0, coupon.UsageCount);
        Assert.False(coupon.IsExhausted);
        var released = DomainAssert.Raised<CouponReleased>(coupon);
        Assert.Equal(
            (Tenant, coupon.Id, _promotion.PromotionId, coupon.Code, order, 0, 1, FakeClock.DefaultNow),
            (released.TenantId, released.CouponId, released.PromotionId, released.Code, released.OrderId, released.UsageCount, released.RemainingUses, released.OccurredAtUtc));

        // L'utilisation libérée est de nouveau disponible.
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);
        Assert.Equal(1, coupon.UsageCount);
    }

    [Fact]
    public void Release_rejects_an_order_that_holds_no_use()
    {
        var coupon = NewCoupon();
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);
        coupon.ClearDomainEvents();

        DomainAssert.Throws(CouponErrors.NoUsageForOrder, () => coupon.Release(OrderId.New(), orderHoldsUsage: false, _clock));

        Assert.Equal(1, coupon.UsageCount);
        DomainAssert.NoEvents(coupon);
    }

    [Fact]
    public void Release_is_allowed_on_a_disabled_coupon_and_never_goes_below_zero()
    {
        var coupon = NewCoupon();
        coupon.Redeem(_promotion, OrderId.New(), false, _clock);
        coupon.Disable(_clock);

        coupon.Release(OrderId.New(), true, _clock);
        coupon.Release(OrderId.New(), true, _clock);

        Assert.Equal(0, coupon.UsageCount);
        Assert.Equal(CouponStatus.Disabled, coupon.Status);
    }

    [Fact]
    public void Discount_references_fit_the_orders_format()
    {
        // Une référence de remise Orders fait au plus 64 caractères, insensible à la casse.
        const int OrdersDiscountReferenceMaxLength = 64;

        Assert.InRange(CouponCode.MaxLength, 1, OrdersDiscountReferenceMaxLength);
        Assert.InRange(CouponCode.Create(new string('A', CouponCode.MaxLength)).ToString().Length, 1, OrdersDiscountReferenceMaxLength);
        Assert.InRange(PromotionId.New().ToString().Length, 1, OrdersDiscountReferenceMaxLength);
    }
}

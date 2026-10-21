using Promotions.Application.Commands;
using Promotions.Application.Queries;
using Promotions.Domain;

namespace Promotions.Application;

public sealed class PromotionUseCaseTests
{
    private readonly PromotionsFixture _fixture = new();

    private DateTimeOffset Now => _fixture.Clock.UtcNow;

    private PromotionRulesInput Rules(
        BenefitInput? benefit = null,
        string? currency = "EUR",
        decimal? minimum = null,
        int? limit = null,
        bool requiresCoupon = false,
        IReadOnlyList<OrderType>? types = null,
        DateTimeOffset? until = null) =>
        new(Now.AddDays(-1), until ?? Now.AddDays(30), benefit ?? new BenefitInput(BenefitType.Percentage, Percentage: 10m), currency, minimum,
            OrderTypes: types, UsageLimit: limit, RequiresCoupon: requiresCoupon);

    private async Task<Guid> ActivePromotionAsync(PromotionRulesInput? rules = null, string name = "Happy hour")
    {
        var id = await _fixture.Create.HandleAsync(new CreatePromotionCommand(name, rules ?? Rules()));
        await _fixture.Activate.HandleAsync(new ActivatePromotionCommand(id));
        return id;
    }

    // --- Catalogue

    [Fact]
    public async Task A_promotion_is_created_as_a_draft_with_its_rules_then_activated()
    {
        var id = await _fixture.Create.HandleAsync(new CreatePromotionCommand(
            "  Menu midi ",
            Rules(new BenefitInput(BenefitType.FixedAmount, Amount: 3m), minimum: 15m, types: [OrderType.DineIn])));

        var draft = await _fixture.Get.HandleAsync(new GetPromotionQuery(id));
        await _fixture.Activate.HandleAsync(new ActivatePromotionCommand(id));

        Assert.Equal(("Menu midi", PromotionStatus.Draft), (draft.Name, draft.Status));
        Assert.Equal((BenefitType.FixedAmount, 3m, "EUR", 15m), (draft.Rules.Benefit!.Type, draft.Rules.Benefit.Amount, draft.Rules.CurrencyCode, draft.Rules.MinimumOrderAmount));
        Assert.Equal([OrderType.DineIn], draft.Rules.OrderTypes);
        Assert.Equal(PromotionStatus.Active, (await _fixture.Get.HandleAsync(new GetPromotionQuery(id))).Status);
    }

    [Theory]
    [InlineData(BenefitType.Percentage, null, 5.0)]
    [InlineData(BenefitType.FixedAmount, 10.0, 5.0)]
    [InlineData(BenefitType.FixedAmount, null, null)]
    public async Task An_inconsistent_benefit_is_refused(BenefitType type, double? percentage, double? amount)
    {
        var benefit = new BenefitInput(type, (decimal?)percentage, (decimal?)amount);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(new CreatePromotionCommand("X", Rules(benefit))));

        Assert.Equal(PromotionsApplicationErrors.BenefitInvalid, refused.Error);
    }

    [Fact]
    public async Task An_amount_without_currency_is_refused()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(new CreatePromotionCommand(
            "X", Rules(new BenefitInput(BenefitType.FixedAmount, Amount: 3m), currency: null))));

        Assert.Equal(PromotionsApplicationErrors.BenefitInvalid, refused.Error);
    }

    [Fact]
    public async Task Once_used_only_non_destructive_rule_changes_pass_unless_explicitly_allowed()
    {
        var promotion = await ActivePromotionAsync();
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), promotion));

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangeRules.HandleAsync(
            new ChangePromotionRulesCommand(promotion, Rules(new BenefitInput(BenefitType.Percentage, Percentage: 50m)))));
        await _fixture.ChangeRules.HandleAsync(new ChangePromotionRulesCommand(promotion, Rules(until: Now.AddDays(60))));
        await _fixture.ChangeRules.HandleAsync(new ChangePromotionRulesCommand(
            promotion, Rules(new BenefitInput(BenefitType.Percentage, Percentage: 50m), until: Now.AddDays(60)), RuleChangePolicy.Unrestricted));

        Assert.Equal(PromotionErrors.DestructiveChangeAfterUsage, refused.Error);
        Assert.Equal(50m, _fixture.PromotionById(promotion).Benefit!.Percentage);
    }

    [Fact]
    public async Task A_coupon_code_is_unique_in_the_restaurant_and_normalized()
    {
        var promotion = await ActivePromotionAsync(Rules(requiresCoupon: true));
        var coupon = await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, " ete-26 ", 5));

        var taken = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, "ETE-26", 1)));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var otherPromotion = await ActivePromotionAsync(Rules(requiresCoupon: true));
        await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(otherPromotion, "ETE-26", 1));

        Assert.Equal("ETE-26", _fixture.CouponById(coupon).Code.Value);
        Assert.Equal(CouponErrors.CodeNotUnique, taken.Error);
    }

    // --- Appliquer à une commande

    [Fact]
    public async Task A_promotion_applied_to_a_draft_order_counts_one_usage_and_returns_the_computed_discount()
    {
        var promotion = await ActivePromotionAsync();
        var order = _fixture.NewOrder(lines: [30m, 20m]);

        var applied = await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, promotion));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, promotion)));

        Assert.Equal((5m, "EUR", promotion.ToString()), (applied.DiscountAmount, applied.CurrencyCode, applied.DiscountReference));
        Assert.Equal(1, _fixture.PromotionById(promotion).UsageCount);
        Assert.Equal(PromotionErrors.AlreadyAppliedToOrder, twice.Error);
        DomainAssert.Raised<PromotionApplied>(_fixture.PromotionById(promotion));
    }

    [Fact]
    public async Task A_coupon_presented_by_the_customer_applies_its_promotion_and_spends_a_use()
    {
        var promotion = await ActivePromotionAsync(Rules(requiresCoupon: true));
        var coupon = await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, "BIENVENUE", 1));
        var order = _fixture.NewOrder();

        var withoutCoupon = await Assert.ThrowsAsync<DomainException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, promotion)));
        var applied = await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, CouponCode: "bienvenue"));
        var exhausted = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), CouponCode: "BIENVENUE")));

        Assert.Equal(DiscountIneligibility.CouponRequired, withoutCoupon.Error);
        Assert.Equal((coupon, 4m), (applied.CouponId!.Value, applied.DiscountAmount));
        Assert.Equal(0, _fixture.CouponById(coupon).RemainingUses);
        Assert.Equal(CouponErrors.Exhausted, exhausted.Error);
    }

    [Fact]
    public async Task Applying_needs_exactly_one_target_a_draft_order_and_a_known_coupon()
    {
        var promotion = await ActivePromotionAsync();

        var none = await Assert.ThrowsAsync<DomainException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder())));
        var both = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), promotion, "CODE")));
        var placed = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(acceptsDiscounts: false), promotion)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), CouponCode: "INCONNU")));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(Guid.NewGuid(), promotion)));

        Assert.Equal(PromotionsApplicationErrors.TargetRequired, none.Error);
        Assert.Equal(PromotionsApplicationErrors.TargetRequired, both.Error);
        Assert.Equal(PromotionsApplicationErrors.OrderNotEditable, placed.Error);
        Assert.Equal(0, _fixture.PromotionById(promotion).UsageCount);
    }

    [Fact]
    public async Task An_ineligible_order_or_a_reached_limit_is_refused_with_a_stable_reason()
    {
        var minimum = await ActivePromotionAsync(Rules(minimum: 100m));
        var limited = await ActivePromotionAsync(Rules(limit: 1));
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), limited));

        var tooSmall = await Assert.ThrowsAsync<DomainException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), minimum)));
        var reached = await Assert.ThrowsAsync<DomainException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), limited)));

        Assert.Equal(DiscountIneligibility.MinimumOrderAmountNotReached, tooSmall.Error);
        Assert.Equal(PromotionErrors.UsageLimitReached, reached.Error);
    }

    [Fact]
    public async Task The_preview_shows_what_each_promotion_would_give_without_recording_anything()
    {
        var tenPercent = await ActivePromotionAsync(name: "Dix pour cent");
        var minimum = await ActivePromotionAsync(Rules(minimum: 100m), name: "Gros panier");
        var couponOnly = await ActivePromotionAsync(Rules(new BenefitInput(BenefitType.FixedAmount, Amount: 8m), requiresCoupon: true), name: "Coupon");
        await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(couponOnly, "HUIT", 3));
        var order = _fixture.NewOrder(lines: [50m]);

        var previews = await _fixture.Preview.HandleAsync(new PreviewOrderDiscountsQuery(order, "huit"));

        Assert.Equal([couponOnly, tenPercent, minimum], previews.Select(preview => preview.PromotionId));
        Assert.Equal((true, 8m), (previews[0].IsEligible, previews[0].Discount));
        Assert.Equal((true, 5m), (previews[1].IsEligible, previews[1].Discount));
        Assert.Equal((false, DiscountIneligibility.MinimumOrderAmountNotReached.Code), (previews[2].IsEligible, previews[2].Reason));
        Assert.Empty(_fixture.Usages);
    }

    [Fact]
    public async Task A_coupon_is_previewed_on_a_cart_before_the_order_exists_without_counting_anything()
    {
        var promotion = await ActivePromotionAsync(Rules(new BenefitInput(BenefitType.FixedAmount, Amount: 8m), minimum: 20m, requiresCoupon: true, types: [OrderType.Delivery]));
        await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, "HUIT", 1));
        var location = Guid.NewGuid();
        IReadOnlyList<CartLineInput> cart = [new(Guid.NewGuid(), 15m), new(Guid.NewGuid(), 10m)];

        var valid = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Delivery, "EUR", cart, " huit "));
        var takeaway = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Takeaway, "EUR", cart, "HUIT"));
        var small = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Delivery, "EUR", [new(Guid.NewGuid(), 5m)], "HUIT"));
        var unknown = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Delivery, "EUR", cart, "AUTRE"));
        var malformed = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Delivery, "EUR", cart, "a b"));
        await _fixture.Pause.HandleAsync(new PausePromotionCommand(promotion));
        var paused = await _fixture.PreviewCart.HandleAsync(new PreviewCartCouponQuery(location, OrderType.Delivery, "EUR", cart, "HUIT"));

        Assert.Equal(new CartCouponPreviewDto("HUIT", true, null, promotion, "Happy hour", 8m), valid);
        Assert.Equal(DiscountIneligibility.OrderTypeNotEligible.Code, takeaway.RefusalCode);
        Assert.Equal(DiscountIneligibility.MinimumOrderAmountNotReached.Code, small.RefusalCode);
        Assert.Equal((false, PromotionsApplicationErrors.CouponNotFound.Code), (unknown.IsValid, unknown.RefusalCode));
        Assert.Equal(CouponErrors.CodeCharactersInvalid.Code, malformed.RefusalCode);
        Assert.Equal(PromotionErrors.NotActive.Code, paused.RefusalCode);
        Assert.Equal(0, _fixture.PromotionById(promotion).UsageCount);
        Assert.Empty(_fixture.Usages);
    }

    // --- Annulation et compensation

    [Fact]
    public async Task A_cancelled_order_gives_back_its_promotion_and_coupon_usages_once()
    {
        var promotion = await ActivePromotionAsync(Rules(requiresCoupon: true));
        var coupon = await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, "UNIQUE", 1));
        var order = _fixture.NewOrder();
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, CouponCode: "UNIQUE"));

        var first = await _fixture.Release.HandleAsync(new ReleaseOrderPromotionsCommand(order));
        var replay = await _fixture.Release.HandleAsync(new ReleaseOrderPromotionsCommand(order));

        Assert.Equal((PromotionReactionOutcome.Done, PromotionReactionOutcome.AlreadyDone), (first, replay));
        Assert.Equal(0, _fixture.PromotionById(promotion).UsageCount);
        Assert.Equal(1, _fixture.CouponById(coupon).RemainingUses);
        // L'usage rendu, le code resservira.
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(_fixture.NewOrder(), CouponCode: "UNIQUE"));
    }

    [Fact]
    public async Task A_discount_refused_by_the_order_gives_back_only_the_usages_of_that_promotion()
    {
        var first = await ActivePromotionAsync(name: "A");
        var second = await ActivePromotionAsync(name: "B");
        var order = _fixture.NewOrder();
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, first));
        await _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, second));

        Assert.Equal(PromotionReactionOutcome.Done, await _fixture.Revert.HandleAsync(new RevertPromotionForOrderCommand(order, first)));
        Assert.Equal(PromotionReactionOutcome.AlreadyDone, await _fixture.Revert.HandleAsync(new RevertPromotionForOrderCommand(order, first)));

        Assert.Equal((0, 1), (_fixture.PromotionById(first).UsageCount, _fixture.PromotionById(second).UsageCount));
    }

    // --- Expiration

    [Fact]
    public async Task Promotions_whose_period_has_ended_expire_once()
    {
        var ending = await ActivePromotionAsync(Rules(until: Now.AddHours(1)));
        var running = await ActivePromotionAsync();
        _fixture.Clock.Advance(TimeSpan.FromHours(2));

        var expired = await _fixture.ExpireEnded.HandleAsync(new ExpireEndedPromotionsCommand());
        var replay = await _fixture.ExpireEnded.HandleAsync(new ExpireEndedPromotionsCommand());

        Assert.Equal((1, 0), (expired, replay));
        Assert.Equal(PromotionStatus.Expired, _fixture.PromotionById(ending).Status);
        Assert.Equal(PromotionStatus.Active, _fixture.PromotionById(running).Status);
    }

    // --- Multi-tenant

    [Fact]
    public async Task A_restaurant_never_reads_applies_nor_changes_the_promotions_of_another()
    {
        var promotion = await ActivePromotionAsync();
        await _fixture.CreateCoupon.HandleAsync(new CreateCouponCommand(promotion, "SECRET", 5));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var order = _fixture.NewOrder();

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Get.HandleAsync(new GetPromotionQuery(promotion)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Pause.HandleAsync(new PausePromotionCommand(promotion)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, promotion)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Apply.HandleAsync(new ApplyPromotionToOrderCommand(order, CouponCode: "SECRET")));
        Assert.Empty(await _fixture.List.HandleAsync(new ListPromotionsQuery()));
        Assert.Equal(PromotionStatus.Active, _fixture.PromotionById(promotion).Status);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_but_does_not_write_and_no_restaurant_means_forbidden()
    {
        var promotion = await ActivePromotionAsync();
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Pause.HandleAsync(new PausePromotionCommand(promotion)));
        Assert.Equal(PromotionsApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(0, await _fixture.ExpireEnded.HandleAsync(new ExpireEndedPromotionsCommand()));
        Assert.Equal(PromotionStatus.Active, (await _fixture.Get.HandleAsync(new GetPromotionQuery(promotion))).Status);

        _fixture.CurrentTenantId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Get.HandleAsync(new GetPromotionQuery(promotion)));
    }

    [Fact]
    public async Task Pages_are_bounded()
    {
        var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.List.HandleAsync(new ListPromotionsQuery(Take: 0)));

        Assert.Equal(PromotionsApplicationErrors.PageInvalid, invalid.Error);
    }
}

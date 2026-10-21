using Promotions.Domain;

namespace Promotions.Domain;

public class PromotionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;

    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static Period Month() => Period.Create(Now.AddDays(-1), Now.AddDays(30));

    private static PromotionRules Rules(UsageLimit? limit = null, Period? period = null) =>
        PromotionRules.Create(period ?? Month(), Conditions.None, Benefit.PercentageOff(10m), limit);

    private Promotion Draft(PromotionRules? rules = null) => Promotion.Create(Tenant, "Happy hour", rules ?? Rules(), _clock);

    private Promotion Active(PromotionRules? rules = null)
    {
        var promotion = Draft(rules);
        promotion.Activate(_clock);
        promotion.ClearDomainEvents();
        return promotion;
    }

    /// <summary>Commande de 20 EUR par défaut : les règles par défaut à 10 % donnent une remise de 2 EUR.</summary>
    private static OrderSnapshot Order(decimal total = 20m, OrderId? orderId = null, TenantId? tenant = null) =>
        OrderSnapshot.Create(
            tenant ?? Tenant,
            orderId ?? OrderId.New(),
            Currency.EUR,
            LocationId.New(),
            OrderType.DineIn,
            [OrderLineSnapshot.Create(ProductId.New(), Eur(total))]);

    private Promotion Used(PromotionRules? rules = null)
    {
        var promotion = Active(rules);
        promotion.Apply(Order(), false, _clock);
        promotion.ClearDomainEvents();
        return promotion;
    }

    [Fact]
    public void Create_starts_as_an_unused_draft()
    {
        var promotion = Promotion.Create(Tenant, "  Happy hour  ", Rules(UsageLimit.Of(3)), _clock);

        Assert.Equal(Tenant, promotion.TenantId);
        Assert.Equal("Happy hour", promotion.Name);
        Assert.Equal(PromotionStatus.Draft, promotion.Status);
        Assert.Equal(0, promotion.UsageCount);
        Assert.False(promotion.HasBeenUsed);
        Assert.Equal(Month(), promotion.ValidityPeriod);
        Assert.Equal(Conditions.None, promotion.Conditions);
        Assert.Equal(Benefit.PercentageOff(10m), promotion.Benefit);
        Assert.Equal(UsageLimit.Of(3), promotion.UsageLimit);
        Assert.Equal(Now, promotion.CreatedAtUtc);
        Assert.Null(promotion.ExpiredAtUtc);

        var created = DomainAssert.Raised<PromotionCreated>(promotion);
        Assert.Equal((Tenant, promotion.Id, "Happy hour", Now), (created.TenantId, created.PromotionId, created.Name, created.OccurredAtUtc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string? name) =>
        DomainAssert.Throws(PromotionErrors.NameRequired, () => Promotion.Create(Tenant, name!, Rules(), _clock));

    [Fact]
    public void Create_rejects_a_too_long_name()
    {
        DomainAssert.Throws(
            PromotionErrors.NameTooLong,
            () => Promotion.Create(Tenant, new string('a', Promotion.MaxNameLength + 1), Rules(), _clock));

        Assert.Equal(Promotion.MaxNameLength, Promotion.Create(Tenant, new string('a', Promotion.MaxNameLength), Rules(), _clock).Name.Length);
    }

    [Fact]
    public void Create_accepts_a_draft_without_benefit()
    {
        var promotion = Draft(PromotionRules.Create(Month()));

        Assert.Null(promotion.Benefit);
        Assert.Equal(PromotionStatus.Draft, promotion.Status);
    }

    [Fact]
    public void Activate_makes_a_valid_draft_active()
    {
        var promotion = Draft();
        promotion.ClearDomainEvents();

        promotion.Activate(_clock);

        Assert.Equal(PromotionStatus.Active, promotion.Status);
        var activated = DomainAssert.Raised<PromotionActivated>(promotion);
        Assert.Equal((Tenant, promotion.Id, Month(), Now), (activated.TenantId, activated.PromotionId, activated.ValidityPeriod, activated.OccurredAtUtc));
    }

    [Fact]
    public void Activate_accepts_a_period_that_has_not_started_yet()
    {
        var promotion = Draft(Rules(period: Period.Create(Now.AddDays(5), Now.AddDays(10))));

        promotion.Activate(_clock);

        Assert.Equal(PromotionStatus.Active, promotion.Status);
    }

    [Fact]
    public void Activate_rejects_rules_without_benefit()
    {
        var promotion = Draft(PromotionRules.Create(Month()));
        promotion.ClearDomainEvents();

        DomainAssert.Throws(PromotionErrors.RulesIncomplete, () => promotion.Activate(_clock));

        Assert.Equal(PromotionStatus.Draft, promotion.Status);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void Activate_rejects_a_period_that_has_ended()
    {
        var promotion = Draft();
        promotion.ClearDomainEvents();
        _clock.Set(Month().EndUtc);

        DomainAssert.Throws(PromotionErrors.PeriodEnded, () => promotion.Activate(_clock));

        Assert.Equal(PromotionStatus.Draft, promotion.Status);
        DomainAssert.NotRaised<PromotionActivated>(promotion);
    }

    [Fact]
    public void Activate_accepts_the_last_instant_of_the_period()
    {
        var promotion = Draft();
        _clock.Set(Month().EndUtc.AddTicks(-1));

        promotion.Activate(_clock);

        Assert.Equal(PromotionStatus.Active, promotion.Status);
    }

    [Fact]
    public void Activate_rejects_an_active_or_paused_promotion()
    {
        var active = Active();
        DomainAssert.Throws(PromotionErrors.NotDraft, () => active.Activate(_clock));

        active.Pause(_clock);
        DomainAssert.Throws(PromotionErrors.NotDraft, () => active.Activate(_clock));
        DomainAssert.NotRaised<PromotionActivated>(active);
    }

    [Fact]
    public void Pause_suspends_an_active_promotion()
    {
        var promotion = Active();

        promotion.Pause(_clock);

        Assert.Equal(PromotionStatus.Paused, promotion.Status);
        Assert.Equal(promotion.Id, DomainAssert.Raised<PromotionPaused>(promotion).PromotionId);
    }

    [Fact]
    public void Pause_rejects_a_draft_or_already_paused_promotion()
    {
        var draft = Draft();
        DomainAssert.Throws(PromotionErrors.NotActive, () => draft.Pause(_clock));

        var paused = Active();
        paused.Pause(_clock);
        paused.ClearDomainEvents();
        DomainAssert.Throws(PromotionErrors.NotActive, () => paused.Pause(_clock));
        DomainAssert.NoEvents(paused);
    }

    [Fact]
    public void Resume_reactivates_a_paused_promotion()
    {
        var promotion = Active();
        promotion.Pause(_clock);

        promotion.Resume(_clock);

        Assert.Equal(PromotionStatus.Active, promotion.Status);
        Assert.Equal(promotion.Id, DomainAssert.Raised<PromotionResumed>(promotion).PromotionId);
    }

    [Fact]
    public void Resume_rejects_a_draft_or_active_promotion()
    {
        DomainAssert.Throws(PromotionErrors.NotPaused, () => Draft().Resume(_clock));

        var active = Active();
        DomainAssert.Throws(PromotionErrors.NotPaused, () => active.Resume(_clock));
        DomainAssert.NoEvents(active);
    }

    [Fact]
    public void Resume_rejects_a_period_that_has_ended()
    {
        var promotion = Active();
        promotion.Pause(_clock);
        _clock.Set(Month().EndUtc.AddDays(1));

        DomainAssert.Throws(PromotionErrors.PeriodEnded, () => promotion.Resume(_clock));

        Assert.Equal(PromotionStatus.Paused, promotion.Status);
        DomainAssert.NotRaised<PromotionResumed>(promotion);
    }

    [Fact]
    public void Expire_is_allowed_from_draft_active_and_paused()
    {
        var draft = Draft();
        var active = Used();
        var paused = Active();
        paused.Pause(_clock);

        draft.Expire(_clock);
        active.Expire(_clock);
        paused.Expire(_clock);

        Assert.All(new[] { draft, active, paused }, promotion =>
        {
            Assert.Equal(PromotionStatus.Expired, promotion.Status);
            Assert.Equal(Now, promotion.ExpiredAtUtc);
        });
        var expired = DomainAssert.Raised<PromotionExpired>(active);
        Assert.Equal((Tenant, active.Id, 1), (expired.TenantId, expired.PromotionId, expired.UsageCount));
    }

    [Fact]
    public void Expired_is_terminal()
    {
        var promotion = Active();
        promotion.Expire(_clock);
        promotion.ClearDomainEvents();

        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.Activate(_clock));
        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.Pause(_clock));
        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.Resume(_clock));
        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.Expire(_clock));
        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.Apply(Order(), false, _clock));
        DomainAssert.Throws(PromotionErrors.Expired, () => promotion.ChangeRules(Rules(UsageLimit.Of(2)), _clock));

        Assert.Equal(PromotionStatus.Expired, promotion.Status);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void Apply_counts_the_usage_and_raises_PromotionApplied()
    {
        var promotion = Active();
        var order = OrderId.New();

        var discount = promotion.Apply(Order(25m, order), false, _clock);

        Assert.Equal(Eur(2.5m), discount);
        Assert.Equal(1, promotion.UsageCount);
        Assert.True(promotion.HasBeenUsed);
        var applied = DomainAssert.Raised<PromotionApplied>(promotion);
        Assert.Equal(
            (Tenant, promotion.Id, order, Eur(2.5m), 1, Now),
            (applied.TenantId, applied.PromotionId, applied.OrderId, applied.Discount, applied.UsageCount, applied.OccurredAtUtc));
    }

    [Fact]
    public void Apply_rejects_a_draft_or_paused_promotion()
    {
        var draft = Draft();
        DomainAssert.Throws(PromotionErrors.NotActive, () => draft.Apply(Order(), false, _clock));

        var paused = Active();
        paused.Pause(_clock);
        DomainAssert.Throws(PromotionErrors.NotActive, () => paused.Apply(Order(), false, _clock));

        Assert.Equal(0, paused.UsageCount);
        DomainAssert.NotRaised<PromotionApplied>(paused);
    }

    [Fact]
    public void Apply_rejects_usage_before_the_period_starts()
    {
        var promotion = Active(Rules(period: Period.Create(Now.AddDays(1), Now.AddDays(10))));

        DomainAssert.Throws(PromotionErrors.OutsideValidityPeriod, () => promotion.Apply(Order(), false, _clock));

        _clock.Set(Now.AddDays(1));
        promotion.Apply(Order(), false, _clock);
        Assert.Equal(1, promotion.UsageCount);
    }

    [Fact]
    public void Apply_rejects_usage_once_the_period_has_ended()
    {
        var promotion = Active();
        _clock.Set(Month().EndUtc);

        DomainAssert.Throws(PromotionErrors.OutsideValidityPeriod, () => promotion.Apply(Order(), false, _clock));

        Assert.Equal(0, promotion.UsageCount);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void Apply_stops_at_the_usage_limit()
    {
        var promotion = Active(Rules(UsageLimit.Of(2)));

        promotion.Apply(Order(), false, _clock);
        Assert.False(promotion.IsUsageLimitReached);
        promotion.Apply(Order(), false, _clock);
        Assert.True(promotion.IsUsageLimitReached);
        promotion.ClearDomainEvents();

        DomainAssert.Throws(PromotionErrors.UsageLimitReached, () => promotion.Apply(Order(), false, _clock));

        Assert.Equal(2, promotion.UsageCount);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void Apply_is_unlimited_without_usage_limit()
    {
        var promotion = Active();

        for (var i = 0; i < 50; i++)
        {
            promotion.Apply(Order(), false, _clock);
        }

        Assert.Equal(50, promotion.UsageCount);
        Assert.False(promotion.IsUsageLimitReached);
    }

    [Fact]
    public void Apply_computes_the_discount_from_the_rules_not_from_the_caller()
    {
        var rules = PromotionRules.Create(Month(), Conditions.None, Benefit.PercentageOff(50m, Eur(8m)));
        var promotion = Active(rules);

        var discount = promotion.Apply(Order(100m), false, _clock);

        Assert.Equal(Eur(8m), discount);
        Assert.Equal(DiscountPolicy.Evaluate(Order(100m), rules, Now).Discount, discount);
        Assert.Equal(Eur(8m), DomainAssert.Raised<PromotionApplied>(promotion).Discount);
    }

    [Fact]
    public void Apply_rejects_an_order_of_another_tenant()
    {
        var promotion = Active();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => promotion.Apply(Order(tenant: TenantId.New()), false, _clock));

        Assert.Equal(0, promotion.UsageCount);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void Apply_rejects_an_ineligible_order_with_the_evaluation_reason()
    {
        var minimum = Active(PromotionRules.Create(Month(), Conditions.Create(minimumOrderAmount: Eur(50m)), Benefit.PercentageOff(10m)));
        DomainAssert.Throws(DiscountIneligibility.MinimumOrderAmountNotReached, () => minimum.Apply(Order(49.99m), false, _clock));

        var delivery = Active(PromotionRules.Create(Month(), Conditions.Create(orderTypes: [OrderType.Delivery]), Benefit.PercentageOff(10m)));
        DomainAssert.Throws(DiscountIneligibility.OrderTypeNotEligible, () => delivery.Apply(Order(), false, _clock));

        var rounding = Active();
        DomainAssert.Throws(DiscountIneligibility.ZeroDiscount, () => rounding.Apply(Order(0.04m), false, _clock));

        Assert.All(new[] { minimum, delivery, rounding }, promotion =>
        {
            Assert.Equal(0, promotion.UsageCount);
            DomainAssert.NoEvents(promotion);
        });
    }

    [Fact]
    public void Apply_of_a_coupon_only_promotion_needs_the_coupon()
    {
        var promotion = Active(Rules().WithRequiresCoupon(true));

        DomainAssert.Throws(DiscountIneligibility.CouponRequired, () => promotion.Apply(Order(), false, _clock));
        Assert.Equal(0, promotion.UsageCount);

        Assert.Equal(Eur(2m), promotion.Apply(Order().WithCouponPresented(true), false, _clock));
        Assert.Equal(1, promotion.UsageCount);
    }

    [Fact]
    public void Apply_rejects_an_order_already_counted()
    {
        var promotion = Used();

        // Rejeu de la même commande : le registre répond « déjà comptée », quel que soit l'état de la promotion.
        DomainAssert.Throws(PromotionErrors.AlreadyAppliedToOrder, () => promotion.Apply(Order(), orderAlreadyCounted: true, _clock));
        promotion.Expire(_clock);
        promotion.ClearDomainEvents();
        DomainAssert.Throws(PromotionErrors.AlreadyAppliedToOrder, () => promotion.Apply(Order(), orderAlreadyCounted: true, _clock));

        Assert.Equal(1, promotion.UsageCount);
        DomainAssert.NoEvents(promotion);
        Assert.Equal("promotions.promotion.already_applied_to_order", PromotionErrors.AlreadyAppliedToOrder.Code);
    }

    [Fact]
    public void RevertUsage_gives_the_usage_back_and_raises_PromotionUsageReverted()
    {
        var promotion = Active(Rules(UsageLimit.Of(1)));
        var order = OrderId.New();
        promotion.Apply(Order(orderId: order), false, _clock);
        promotion.ClearDomainEvents();
        Assert.True(promotion.IsUsageLimitReached);

        promotion.RevertUsage(order, orderHoldsUsage: true, _clock);

        Assert.Equal(0, promotion.UsageCount);
        Assert.False(promotion.IsUsageLimitReached);
        var reverted = DomainAssert.Raised<PromotionUsageReverted>(promotion);
        Assert.Equal(
            (Tenant, promotion.Id, order, 0, Now),
            (reverted.TenantId, reverted.PromotionId, reverted.OrderId, reverted.UsageCount, reverted.OccurredAtUtc));

        // L'utilisation libérée est de nouveau disponible.
        promotion.Apply(Order(), false, _clock);
        Assert.Equal(1, promotion.UsageCount);
    }

    [Fact]
    public void RevertUsage_rejects_an_order_that_holds_no_usage()
    {
        var promotion = Used();

        DomainAssert.Throws(PromotionErrors.NoUsageForOrder, () => promotion.RevertUsage(OrderId.New(), orderHoldsUsage: false, _clock));

        Assert.Equal(1, promotion.UsageCount);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void RevertUsage_is_allowed_in_every_status()
    {
        var paused = Used();
        paused.Pause(_clock);
        var expired = Used();
        expired.Expire(_clock);

        paused.RevertUsage(OrderId.New(), true, _clock);
        expired.RevertUsage(OrderId.New(), true, _clock);

        Assert.Equal((0, PromotionStatus.Paused), (paused.UsageCount, paused.Status));
        Assert.Equal((0, PromotionStatus.Expired), (expired.UsageCount, expired.Status));
        DomainAssert.Raised<PromotionUsageReverted>(expired);
    }

    [Fact]
    public void RevertUsage_never_drives_the_counter_below_zero()
    {
        var promotion = Active();

        promotion.RevertUsage(OrderId.New(), true, _clock);

        Assert.Equal(0, promotion.UsageCount);
    }

    [Fact]
    public void ChangeRules_is_free_while_the_promotion_is_unused()
    {
        var promotion = Active(Rules(UsageLimit.Of(10)));
        var newRules = PromotionRules.Create(
            Period.Create(Now, Now.AddDays(3)),
            Conditions.Create(minimumOrderAmount: Eur(15m)),
            Benefit.FixedAmountOff(Eur(3m)),
            UsageLimit.Of(1));

        promotion.ChangeRules(newRules, _clock);

        Assert.Equal(newRules, promotion.Rules);
        var changed = DomainAssert.Raised<PromotionRulesChanged>(promotion);
        Assert.Equal((Tenant, promotion.Id, newRules), (changed.TenantId, changed.PromotionId, changed.Rules));
    }

    [Fact]
    public void ChangeRules_with_identical_rules_is_a_silent_no_op()
    {
        var draft = Draft();
        draft.ClearDomainEvents();
        var used = Used();

        draft.ChangeRules(Rules(), _clock);
        used.ChangeRules(Rules(), _clock, RuleChangePolicy.Frozen);

        Assert.Equal(Rules(), draft.Rules);
        DomainAssert.NoEvents(draft);
        DomainAssert.NoEvents(used);
    }

    [Fact]
    public void ChangeRules_default_policy_rejects_changing_the_coupon_requirement_after_usage()
    {
        var promotion = Used();

        DomainAssert.Throws(
            PromotionErrors.DestructiveChangeAfterUsage,
            () => promotion.ChangeRules(promotion.Rules.WithRequiresCoupon(true), _clock));
    }

    [Fact]
    public void ChangeRules_lets_a_draft_drop_its_benefit_or_use_a_past_period()
    {
        var promotion = Draft();

        promotion.ChangeRules(PromotionRules.Create(Period.Create(Now.AddDays(-10), Now.AddDays(-5))), _clock);

        Assert.Null(promotion.Benefit);
        DomainAssert.Throws(PromotionErrors.RulesIncomplete, () => promotion.Activate(_clock));
    }

    [Fact]
    public void ChangeRules_keeps_an_activated_promotion_valid()
    {
        var active = Active();
        var paused = Active();
        paused.Pause(_clock);
        paused.ClearDomainEvents();

        foreach (var promotion in new[] { active, paused })
        {
            DomainAssert.Throws(PromotionErrors.RulesIncomplete, () => promotion.ChangeRules(Rules().WithBenefit(null), _clock));
            DomainAssert.Throws(
                PromotionErrors.PeriodEnded,
                () => promotion.ChangeRules(Rules(period: Period.Create(Now.AddDays(-10), Now)), _clock));
            Assert.Equal(Rules(), promotion.Rules);
            DomainAssert.NoEvents(promotion);
        }
    }

    [Fact]
    public void ChangeRules_default_policy_accepts_a_later_end_and_a_higher_limit_after_usage()
    {
        var promotion = Used(Rules(UsageLimit.Of(5)));
        var extended = Rules(UsageLimit.Of(8), Period.Create(Month().StartUtc, Month().EndUtc.AddDays(15)));

        promotion.ChangeRules(extended, _clock);

        Assert.Equal(extended, promotion.Rules);
        DomainAssert.Raised<PromotionRulesChanged>(promotion);

        promotion.ChangeRules(extended.WithUsageLimit(null), _clock);
        Assert.Null(promotion.UsageLimit);
    }

    [Fact]
    public void ChangeRules_default_policy_rejects_destructive_changes_after_usage()
    {
        var promotion = Used(Rules(UsageLimit.Of(5)));
        var current = promotion.Rules;
        PromotionRules[] destructive =
        [
            current.WithBenefit(Benefit.PercentageOff(5m)),
            current.WithBenefit(Benefit.FixedAmountOff(Eur(1m))),
            current.WithConditions(Conditions.Create(minimumOrderAmount: Eur(50m))),
            current.WithConditions(Conditions.Create(orderTypes: [OrderType.Delivery])),
            current.WithValidityPeriod(Period.Create(Month().StartUtc, Month().EndUtc.AddDays(-1))),
            current.WithValidityPeriod(Period.Create(Month().StartUtc.AddHours(1), Month().EndUtc)),
            current.WithUsageLimit(UsageLimit.Of(4)),
        ];

        foreach (var rules in destructive)
        {
            DomainAssert.Throws(PromotionErrors.DestructiveChangeAfterUsage, () => promotion.ChangeRules(rules, _clock));
        }

        Assert.Equal(current, promotion.Rules);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void ChangeRules_default_policy_rejects_introducing_a_limit_after_usage()
    {
        var promotion = Used();

        DomainAssert.Throws(
            PromotionErrors.DestructiveChangeAfterUsage,
            () => promotion.ChangeRules(promotion.Rules.WithUsageLimit(UsageLimit.Of(100)), _clock));
    }

    [Fact]
    public void ChangeRules_frozen_policy_rejects_any_change_after_usage()
    {
        var promotion = Used(Rules(UsageLimit.Of(5)));

        DomainAssert.Throws(
            PromotionErrors.RulesLockedAfterUsage,
            () => promotion.ChangeRules(promotion.Rules.WithUsageLimit(UsageLimit.Of(9)), _clock, RuleChangePolicy.Frozen));

        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void ChangeRules_frozen_policy_does_not_block_an_unused_promotion()
    {
        var promotion = Active();

        promotion.ChangeRules(Rules().WithBenefit(Benefit.PercentageOff(20m)), _clock, RuleChangePolicy.Frozen);

        Assert.Equal(Benefit.PercentageOff(20m), promotion.Benefit);
    }

    [Fact]
    public void ChangeRules_unrestricted_policy_accepts_destructive_changes_after_usage()
    {
        var promotion = Used(Rules(UsageLimit.Of(5)));
        var newRules = promotion.Rules.WithBenefit(Benefit.FixedAmountOff(Eur(1m))).WithUsageLimit(UsageLimit.Of(1));

        promotion.ChangeRules(newRules, _clock, RuleChangePolicy.Unrestricted);

        Assert.Equal(newRules, promotion.Rules);
        Assert.True(promotion.IsUsageLimitReached);
    }

    [Fact]
    public void ChangeRules_never_sets_a_limit_below_the_recorded_usages()
    {
        var promotion = Active(Rules(UsageLimit.Of(5)));
        promotion.Apply(Order(), false, _clock);
        promotion.Apply(Order(), false, _clock);
        promotion.ClearDomainEvents();

        DomainAssert.Throws(
            PromotionErrors.UsageLimitBelowUsage,
            () => promotion.ChangeRules(promotion.Rules.WithUsageLimit(UsageLimit.Of(1)), _clock, RuleChangePolicy.Unrestricted));

        Assert.Equal(UsageLimit.Of(5), promotion.UsageLimit);
        DomainAssert.NoEvents(promotion);
    }

    [Fact]
    public void ChangeRules_rejects_an_unknown_policy()
    {
        var used = Used();
        DomainAssert.Throws(
            PromotionErrors.RuleChangePolicyUnknown,
            () => used.ChangeRules(Rules(UsageLimit.Of(3)), _clock, (RuleChangePolicy)42));
        DomainAssert.NoEvents(used);

        // Rejetée même quand la policy ne serait pas lue (promotion inutilisée, règles identiques).
        var draft = Draft();
        DomainAssert.Throws(
            PromotionErrors.RuleChangePolicyUnknown,
            () => draft.ChangeRules(Rules(UsageLimit.Of(3)), _clock, (RuleChangePolicy)0));
        DomainAssert.Throws(
            PromotionErrors.RuleChangePolicyUnknown,
            () => draft.ChangeRules(draft.Rules, _clock, (RuleChangePolicy)0));
        Assert.Equal("promotions.promotion.rule_change_policy_unknown", PromotionErrors.RuleChangePolicyUnknown.Code);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Promotion.Create(default, "Happy hour", Rules(), _clock));

    [Fact]
    public void Operations_read_the_clock_once()
    {
        var clock = new TickingClock();

        var promotion = Promotion.Create(Tenant, "Happy hour", Rules(), clock);
        Assert.Equal(promotion.CreatedAtUtc, DomainAssert.Raised<PromotionCreated>(promotion).OccurredAtUtc);
        Assert.Equal(1, clock.Reads);

        promotion.Activate(clock);
        Assert.Equal(2, clock.Reads);

        promotion.Apply(Order(), false, clock);
        Assert.Equal(3, clock.Reads);

        promotion.ChangeRules(promotion.Rules.WithUsageLimit(UsageLimit.Of(9)), clock, RuleChangePolicy.Unrestricted);
        Assert.Equal(4, clock.Reads);

        promotion.Pause(clock);
        promotion.Resume(clock);
        Assert.Equal(6, clock.Reads);

        promotion.ClearDomainEvents();
        promotion.Expire(clock);
        Assert.Equal(7, clock.Reads);
        Assert.Equal(promotion.ExpiredAtUtc, DomainAssert.Raised<PromotionExpired>(promotion).OccurredAtUtc);
    }

    [Fact]
    public void ToReference_exposes_tenant_id_and_expiry()
    {
        var promotion = Active();
        var reference = promotion.ToReference();
        Assert.Equal((Tenant, promotion.Id, false), (reference.TenantId, reference.PromotionId, reference.IsExpired));
        Assert.Equal(reference, promotion.ToReference());

        promotion.Expire(_clock);
        Assert.True(promotion.ToReference().IsExpired);
    }
}

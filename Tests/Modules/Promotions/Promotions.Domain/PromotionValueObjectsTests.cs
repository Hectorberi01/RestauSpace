using Promotions.Domain;

namespace Promotions.Domain;

public class PromotionValueObjectsTests
{
    private static readonly DateTimeOffset Start = FakeClock.DefaultNow;

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static Period Month() => Period.Create(Start, Start.AddDays(30));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UsageLimit_rejects_zero_or_negative(int value) =>
        DomainAssert.Throws(PromotionErrors.UsageLimitNotPositive, () => UsageLimit.Of(value));

    [Fact]
    public void UsageLimit_is_compared_by_value()
    {
        Assert.Equal(UsageLimit.Of(3), UsageLimit.Of(3));
        Assert.NotEqual(UsageLimit.Of(3), UsageLimit.Of(4));
        Assert.Equal(3, UsageLimit.Of(3).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    public void Percentage_benefit_rejects_out_of_range_values(decimal percentage) =>
        DomainAssert.Throws(PromotionErrors.PercentageOutOfRange, () => Benefit.PercentageOff(percentage));

    [Theory]
    [InlineData(0.01)]
    [InlineData(100)]
    public void Percentage_benefit_accepts_the_bounds(decimal percentage)
    {
        var benefit = Benefit.PercentageOff(percentage);

        Assert.Equal(BenefitType.Percentage, benefit.Type);
        Assert.Equal(percentage, benefit.Percentage);
        Assert.Null(benefit.Amount);
        Assert.Null(benefit.MaxDiscount);
        Assert.Null(benefit.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Percentage_benefit_rejects_a_non_positive_cap(decimal cap) =>
        DomainAssert.Throws(PromotionErrors.MaxDiscountNotPositive, () => Benefit.PercentageOff(10m, Eur(cap)));

    [Fact]
    public void Capped_percentage_benefit_is_bound_to_the_cap_currency() =>
        Assert.Equal(Currency.EUR, Benefit.PercentageOff(10m, Eur(5m)).Currency);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Fixed_benefit_rejects_a_non_positive_amount(decimal amount) =>
        DomainAssert.Throws(PromotionErrors.FixedAmountNotPositive, () => Benefit.FixedAmountOff(Eur(amount)));

    [Fact]
    public void Fixed_benefit_keeps_its_amount()
    {
        var benefit = Benefit.FixedAmountOff(Eur(5m));

        Assert.Equal(BenefitType.FixedAmount, benefit.Type);
        Assert.Equal(Eur(5m), benefit.Amount);
        Assert.Null(benefit.Percentage);
        Assert.Equal(Currency.EUR, benefit.Currency);
    }

    [Fact]
    public void Benefits_are_compared_by_value()
    {
        Assert.Equal(Benefit.PercentageOff(10m, Eur(5m)), Benefit.PercentageOff(10m, Eur(5m)));
        Assert.NotEqual(Benefit.PercentageOff(10m), Benefit.PercentageOff(10m, Eur(5m)));
        Assert.NotEqual(Benefit.PercentageOff(10m), Benefit.FixedAmountOff(Eur(10m)));
    }

    [Fact]
    public void ComputeDiscount_rounds_a_percentage_half_away_from_zero() =>
        Assert.Equal(Eur(1.67m), Benefit.PercentageOff(33.33m).ComputeDiscount(Eur(5m)));

    [Fact]
    public void ComputeDiscount_never_exceeds_the_cap() =>
        Assert.Equal(Eur(5m), Benefit.PercentageOff(50m, Eur(5m)).ComputeDiscount(Eur(100m)));

    [Fact]
    public void ComputeDiscount_never_exceeds_the_eligible_total()
    {
        Assert.Equal(Eur(4m), Benefit.FixedAmountOff(Eur(10m)).ComputeDiscount(Eur(4m)));
        Assert.Equal(Eur(4m), Benefit.PercentageOff(100m, Eur(50m)).ComputeDiscount(Eur(4m)));
    }

    [Fact]
    public void ComputeDiscount_is_zero_on_an_empty_eligible_total() =>
        Assert.Equal(Eur(0m), Benefit.FixedAmountOff(Eur(10m)).ComputeDiscount(Eur(0m)));

    [Fact]
    public void ComputeDiscount_rejects_another_currency() =>
        DomainAssert.Throws(
            CommonErrors.CurrencyMismatch,
            () => Benefit.FixedAmountOff(Eur(10m)).ComputeDiscount(Money.Of(20m, Currency.USD)));

    [Fact]
    public void Conditions_none_has_no_restriction()
    {
        var none = Conditions.None;

        Assert.Null(none.MinimumOrderAmount);
        Assert.Empty(none.ProductIds);
        Assert.Empty(none.LocationIds);
        Assert.Empty(none.OrderTypes);
        Assert.True(none.AppliesToProduct(ProductId.New()));
        Assert.True(none.AppliesToLocation(LocationId.New()));
        Assert.True(none.AppliesToOrderType(OrderType.Delivery));
        Assert.Equal(Conditions.None, Conditions.Create());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Conditions_reject_a_non_positive_minimum_order_amount(decimal amount) =>
        DomainAssert.Throws(
            PromotionErrors.MinimumOrderAmountNotPositive,
            () => Conditions.Create(minimumOrderAmount: Eur(amount)));

    [Fact]
    public void Conditions_reject_an_unknown_order_type() =>
        DomainAssert.Throws(PromotionErrors.OrderTypeInvalid, () => Conditions.Create(orderTypes: [(OrderType)99]));

    [Fact]
    public void Conditions_reject_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Conditions.Create(productIds: [ProductId.New(), default]));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Conditions.Create(locationIds: [default]));
    }

    [Fact]
    public void Conditions_restrict_products_locations_and_order_types()
    {
        var product = ProductId.New();
        var location = LocationId.New();
        var conditions = Conditions.Create(Eur(20m), [product], [location], [OrderType.Takeaway]);

        Assert.Equal(Eur(20m), conditions.MinimumOrderAmount);
        Assert.True(conditions.AppliesToProduct(product));
        Assert.False(conditions.AppliesToProduct(ProductId.New()));
        Assert.True(conditions.AppliesToLocation(location));
        Assert.False(conditions.AppliesToLocation(LocationId.New()));
        Assert.True(conditions.AppliesToOrderType(OrderType.Takeaway));
        Assert.False(conditions.AppliesToOrderType(OrderType.DineIn));
    }

    [Fact]
    public void Conditions_ignore_duplicates_and_input_order()
    {
        var first = ProductId.New();
        var second = ProductId.New();

        var left = Conditions.Create(productIds: [first, second, first], orderTypes: [OrderType.Delivery, OrderType.DineIn]);
        var right = Conditions.Create(productIds: [second, first], orderTypes: [OrderType.DineIn, OrderType.Delivery, OrderType.DineIn]);

        Assert.Equal(left, right);
        Assert.Equal(2, left.ProductIds.Count);
        Assert.Equal(2, left.OrderTypes.Count);
    }

    [Fact]
    public void Conditions_with_different_sets_are_not_equal()
    {
        var id = Guid.CreateVersion7();

        Assert.NotEqual(Conditions.Create(productIds: [ProductId.From(id)]), Conditions.None);
        Assert.NotEqual(
            Conditions.Create(productIds: [ProductId.From(id)]),
            Conditions.Create(productIds: [ProductId.New()]));
        Assert.NotEqual(Conditions.Create(locationIds: [LocationId.New()]), Conditions.Create(locationIds: [LocationId.New()]));
    }

    [Fact]
    public void Rules_reject_a_period_whose_start_is_not_before_its_end()
    {
        DomainAssert.Throws(CommonErrors.PeriodInvalid, () => PromotionRules.Create(Period.Create(Start, Start)));
        DomainAssert.Throws(CommonErrors.PeriodInvalid, () => PromotionRules.Create(Period.Create(Start, Start.AddSeconds(-1))));
    }

    [Fact]
    public void Rules_default_to_no_condition_no_benefit_and_no_limit()
    {
        var rules = PromotionRules.Create(Month());

        Assert.Equal(Conditions.None, rules.Conditions);
        Assert.Null(rules.Benefit);
        Assert.Null(rules.UsageLimit);
        Assert.Null(rules.Currency);
        Assert.False(rules.IsComplete);
        Assert.False(rules.RequiresCoupon);
    }

    [Fact]
    public void Rules_keep_the_coupon_requirement_and_compare_it()
    {
        var open = PromotionRules.Create(Month(), benefit: Benefit.PercentageOff(10m));
        var couponOnly = PromotionRules.Create(Month(), benefit: Benefit.PercentageOff(10m), requiresCoupon: true);

        Assert.True(couponOnly.RequiresCoupon);
        Assert.NotEqual(open, couponOnly);
        Assert.Equal(couponOnly, open.WithRequiresCoupon(true));
        Assert.True(couponOnly.WithUsageLimit(UsageLimit.Of(2)).WithValidityPeriod(Month()).WithConditions(Conditions.None).WithBenefit(null).RequiresCoupon);
    }

    [Fact]
    public void Rules_with_a_benefit_are_complete()
    {
        var rules = PromotionRules.Create(Month(), benefit: Benefit.FixedAmountOff(Eur(5m)));

        Assert.True(rules.IsComplete);
        Assert.Equal(Currency.EUR, rules.Currency);
    }

    [Fact]
    public void Rules_reject_amounts_in_different_currencies()
    {
        var minimumInUsd = Conditions.Create(minimumOrderAmount: Money.Of(20m, Currency.USD));

        DomainAssert.Throws(
            PromotionErrors.RulesCurrencyMismatch,
            () => PromotionRules.Create(Month(), minimumInUsd, Benefit.FixedAmountOff(Eur(5m))));
        DomainAssert.Throws(
            PromotionErrors.RulesCurrencyMismatch,
            () => PromotionRules.Create(Month(), minimumInUsd, Benefit.PercentageOff(10m, Eur(5m))));
        DomainAssert.Throws(
            PromotionErrors.RulesCurrencyMismatch,
            () => PromotionRules.Create(Month(), benefit: Benefit.FixedAmountOff(Eur(5m))).WithConditions(minimumInUsd));
    }

    [Fact]
    public void Rules_accept_an_uncapped_percentage_with_any_minimum_currency()
    {
        var rules = PromotionRules.Create(
            Month(),
            Conditions.Create(minimumOrderAmount: Money.Of(20m, Currency.USD)),
            Benefit.PercentageOff(10m));

        Assert.Equal(Currency.USD, rules.Currency);
    }

    [Fact]
    public void Rules_with_helpers_return_modified_copies()
    {
        var rules = PromotionRules.Create(Month(), benefit: Benefit.PercentageOff(10m), usageLimit: UsageLimit.Of(5));
        var longer = Period.Create(Start, Start.AddDays(60));

        Assert.Equal(longer, rules.WithValidityPeriod(longer).ValidityPeriod);
        Assert.Equal(UsageLimit.Of(9), rules.WithUsageLimit(UsageLimit.Of(9)).UsageLimit);
        Assert.Null(rules.WithBenefit(null).Benefit);
        Assert.Equal(Month(), rules.ValidityPeriod);
        Assert.Equal(rules, PromotionRules.Create(Month(), Conditions.None, Benefit.PercentageOff(10m), UsageLimit.Of(5)));
        Assert.NotEqual(rules, rules.WithUsageLimit(null));
    }

    [Fact]
    public void Ids_reject_empty_guids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => PromotionId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => CouponId.From(Guid.Empty));

        var guid = Guid.CreateVersion7();
        Assert.Equal(guid, PromotionId.From(guid).Value);
        Assert.Equal(guid.ToString(), CouponId.From(guid).ToString());
        Assert.NotEqual(PromotionId.New(), PromotionId.New());
    }
}

using Promotions.Domain;

namespace Promotions.Domain;

public class DiscountPolicyTests
{
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;
    private static readonly Period Month = Period.Create(Now.AddDays(-1), Now.AddDays(30));
    private static readonly ProductId Pizza = ProductId.New();
    private static readonly ProductId Drink = ProductId.New();
    private static readonly LocationId Paris = LocationId.New();
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly OrderId TheOrder = OrderId.New();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static OrderSnapshot Order(decimal pizza = 30m, decimal drink = 10m, OrderType type = OrderType.DineIn) =>
        OrderSnapshot.Create(
            Tenant,
            TheOrder,
            Currency.EUR,
            Paris,
            type,
            [OrderLineSnapshot.Create(Pizza, Eur(pizza)), OrderLineSnapshot.Create(Drink, Eur(drink))]);

    private static PromotionRules Rules(Benefit? benefit, Conditions? conditions = null) =>
        PromotionRules.Create(Month, conditions, benefit);

    private static void AssertNotEligible(DomainError reason, DiscountEvaluation result)
    {
        Assert.False(result.IsEligible);
        Assert.Equal(reason.Code, result.ReasonCode);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(Eur(0m), result.Discount);
    }

    [Fact]
    public void Percentage_applies_to_the_whole_order_without_product_restriction()
    {
        var result = DiscountPolicy.Evaluate(Order(), Rules(Benefit.PercentageOff(10m)), Now);

        Assert.True(result.IsEligible);
        Assert.Null(result.Reason);
        Assert.Null(result.ReasonCode);
        Assert.Equal(Eur(40m), result.EligibleTotal);
        Assert.Equal(Eur(4m), result.Discount);
    }

    [Fact]
    public void Percentage_applies_to_eligible_lines_only()
    {
        var rules = Rules(Benefit.PercentageOff(50m), Conditions.Create(productIds: [Pizza]));

        var result = DiscountPolicy.Evaluate(Order(), rules, Now);

        Assert.Equal(Eur(30m), result.EligibleTotal);
        Assert.Equal(Eur(15m), result.Discount);
    }

    [Fact]
    public void Percentage_is_rounded_to_the_currency_precision()
    {
        var result = DiscountPolicy.Evaluate(Order(pizza: 9.99m, drink: 0m), Rules(Benefit.PercentageOff(15m)), Now);

        // 9.99 * 15 % = 1.4985
        Assert.Equal(Eur(1.50m), result.Discount);
    }

    [Fact]
    public void Percentage_is_rounded_for_a_zero_decimal_currency()
    {
        var order = OrderSnapshot.Create(
            Tenant,
            TheOrder,
            Currency.XOF,
            Paris,
            OrderType.DineIn,
            [OrderLineSnapshot.Create(Pizza, Money.Of(1005m, Currency.XOF))]);

        var result = DiscountPolicy.Evaluate(order, Rules(Benefit.PercentageOff(10m)), Now);

        Assert.Equal(Money.Of(101m, Currency.XOF), result.Discount);
    }

    [Fact]
    public void Percentage_discount_is_capped_by_the_maximum_discount()
    {
        var result = DiscountPolicy.Evaluate(Order(), Rules(Benefit.PercentageOff(50m, Eur(8m))), Now);

        Assert.True(result.IsEligible);
        Assert.Equal(Eur(8m), result.Discount);
    }

    [Fact]
    public void Cap_does_not_change_a_discount_below_it()
    {
        var result = DiscountPolicy.Evaluate(Order(), Rules(Benefit.PercentageOff(10m, Eur(8m))), Now);

        Assert.Equal(Eur(4m), result.Discount);
    }

    [Fact]
    public void Fixed_discount_never_exceeds_the_eligible_total()
    {
        var rules = Rules(Benefit.FixedAmountOff(Eur(25m)), Conditions.Create(productIds: [Drink]));

        var result = DiscountPolicy.Evaluate(Order(), rules, Now);

        Assert.True(result.IsEligible);
        Assert.Equal(Eur(10m), result.EligibleTotal);
        Assert.Equal(Eur(10m), result.Discount);
    }

    [Fact]
    public void Cap_higher_than_the_eligible_total_is_bounded_by_the_eligible_total()
    {
        var rules = Rules(Benefit.PercentageOff(100m, Eur(500m)), Conditions.Create(productIds: [Drink]));

        var result = DiscountPolicy.Evaluate(Order(), rules, Now);

        Assert.Equal(Eur(10m), result.Discount);
    }

    [Fact]
    public void Fixed_discount_below_the_eligible_total_is_applied_in_full()
    {
        var result = DiscountPolicy.Evaluate(Order(), Rules(Benefit.FixedAmountOff(Eur(5m))), Now);

        Assert.Equal(Eur(5m), result.Discount);
    }

    [Fact]
    public void Discount_never_exceeds_the_eligible_total_whatever_the_benefit()
    {
        Benefit[] benefits =
        [
            Benefit.PercentageOff(100m),
            Benefit.PercentageOff(99.99m, Eur(1000m)),
            Benefit.FixedAmountOff(Eur(0.01m)),
            Benefit.FixedAmountOff(Eur(1000m)),
        ];
        decimal[] totals = [0.01m, 0.05m, 9.99m, 100m];

        foreach (var benefit in benefits)
        {
            foreach (var total in totals)
            {
                var result = DiscountPolicy.Evaluate(Order(pizza: total, drink: 3m), Rules(benefit, Conditions.Create(productIds: [Pizza])), Now);

                Assert.True(result.Discount <= result.EligibleTotal);
                Assert.False(result.Discount.IsNegative);
                Assert.Equal(Eur(total), result.EligibleTotal);
            }
        }
    }

    [Fact]
    public void Rules_without_benefit_give_no_discount() =>
        AssertNotEligible(DiscountIneligibility.NoBenefit, DiscountPolicy.Evaluate(Order(), Rules(null), Now));

    [Fact]
    public void Order_outside_the_validity_period_is_not_eligible()
    {
        var rules = Rules(Benefit.PercentageOff(10m));

        AssertNotEligible(DiscountIneligibility.OutsideValidityPeriod, DiscountPolicy.Evaluate(Order(), rules, Month.StartUtc.AddTicks(-1)));
        AssertNotEligible(DiscountIneligibility.OutsideValidityPeriod, DiscountPolicy.Evaluate(Order(), rules, Month.EndUtc));
        Assert.True(DiscountPolicy.Evaluate(Order(), rules, Month.StartUtc).IsEligible);
        Assert.True(DiscountPolicy.Evaluate(Order(), rules, Month.EndUtc.AddTicks(-1)).IsEligible);
    }

    [Fact]
    public void Order_from_another_location_is_not_eligible()
    {
        var rules = Rules(Benefit.PercentageOff(10m), Conditions.Create(locationIds: [LocationId.New()]));

        AssertNotEligible(DiscountIneligibility.LocationNotEligible, DiscountPolicy.Evaluate(Order(), rules, Now));
        Assert.True(DiscountPolicy.Evaluate(Order(), Rules(Benefit.PercentageOff(10m), Conditions.Create(locationIds: [Paris])), Now).IsEligible);
    }

    [Fact]
    public void Order_of_another_type_is_not_eligible()
    {
        var rules = Rules(Benefit.PercentageOff(10m), Conditions.Create(orderTypes: [OrderType.Takeaway, OrderType.Delivery]));

        AssertNotEligible(DiscountIneligibility.OrderTypeNotEligible, DiscountPolicy.Evaluate(Order(type: OrderType.DineIn), rules, Now));
        Assert.True(DiscountPolicy.Evaluate(Order(type: OrderType.Delivery), rules, Now).IsEligible);
    }

    [Fact]
    public void Fixed_benefit_in_another_currency_is_a_stable_ineligibility_reason()
    {
        var rules = Rules(Benefit.FixedAmountOff(Money.Of(5m, Currency.USD)));

        var result = DiscountPolicy.Evaluate(Order(), rules, Now);

        AssertNotEligible(DiscountIneligibility.CurrencyMismatch, result);
        Assert.Equal("promotions.discount.currency_mismatch", result.ReasonCode);
    }

    [Fact]
    public void Cap_or_minimum_in_another_currency_is_not_eligible()
    {
        var cappedInUsd = Rules(Benefit.PercentageOff(10m, Money.Of(5m, Currency.USD)));
        var minimumInUsd = Rules(Benefit.PercentageOff(10m), Conditions.Create(minimumOrderAmount: Money.Of(5m, Currency.USD)));

        AssertNotEligible(DiscountIneligibility.CurrencyMismatch, DiscountPolicy.Evaluate(Order(), cappedInUsd, Now));
        AssertNotEligible(DiscountIneligibility.CurrencyMismatch, DiscountPolicy.Evaluate(Order(), minimumInUsd, Now));
    }

    [Fact]
    public void Minimum_order_amount_is_compared_with_the_whole_order_total()
    {
        var rules = Rules(Benefit.PercentageOff(10m), Conditions.Create(Eur(40m), [Pizza]));

        // Le total de 40 (pizza 30 + boisson 10) atteint le minimum alors que seule la pizza est éligible.
        var reached = DiscountPolicy.Evaluate(Order(), rules, Now);
        Assert.True(reached.IsEligible);
        Assert.Equal(Eur(3m), reached.Discount);

        AssertNotEligible(
            DiscountIneligibility.MinimumOrderAmountNotReached,
            DiscountPolicy.Evaluate(Order(pizza: 29.99m), rules, Now));
    }

    [Fact]
    public void Order_without_eligible_line_is_not_eligible()
    {
        var rules = Rules(Benefit.PercentageOff(10m), Conditions.Create(productIds: [ProductId.New()]));

        var result = DiscountPolicy.Evaluate(Order(), rules, Now);

        AssertNotEligible(DiscountIneligibility.NoEligibleAmount, result);
        Assert.Equal(Eur(0m), result.EligibleTotal);
    }

    [Fact]
    public void Empty_order_is_not_eligible()
    {
        var order = OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, []);

        AssertNotEligible(DiscountIneligibility.NoEligibleAmount, DiscountPolicy.Evaluate(order, Rules(Benefit.FixedAmountOff(Eur(5m))), Now));
    }

    [Fact]
    public void Discount_rounding_to_zero_is_not_eligible()
    {
        var result = DiscountPolicy.Evaluate(Order(pizza: 0.04m, drink: 0m), Rules(Benefit.PercentageOff(10m)), Now);

        AssertNotEligible(DiscountIneligibility.ZeroDiscount, result);
        Assert.Equal(Eur(0.04m), result.EligibleTotal);
    }

    [Fact]
    public void Evaluate_is_deterministic()
    {
        var rules = Rules(Benefit.PercentageOff(12.5m, Eur(4m)), Conditions.Create(Eur(10m), [Pizza], [Paris], [OrderType.DineIn]));

        var first = DiscountPolicy.Evaluate(Order(), rules, Now);
        var second = DiscountPolicy.Evaluate(Order(), rules, Now);

        Assert.Equal(first, second);
        Assert.Equal(Eur(3.75m), first.Discount);
    }

    [Fact]
    public void Evaluate_does_not_depend_on_line_order()
    {
        var rules = Rules(Benefit.PercentageOff(33m));
        var lines = new[] { OrderLineSnapshot.Create(Pizza, Eur(9.99m)), OrderLineSnapshot.Create(Drink, Eur(3.33m)) };

        var forward = DiscountPolicy.Evaluate(OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, lines), rules, Now);
        var backward = DiscountPolicy.Evaluate(OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, lines.Reverse()), rules, Now);

        Assert.Equal(forward.Discount, backward.Discount);
    }

    [Fact]
    public void Snapshot_sums_its_lines()
    {
        var order = Order(pizza: 12.5m, drink: 2.5m);

        Assert.Equal(Eur(15m), order.Total);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(Currency.EUR, order.Currency);
        Assert.Equal(Paris, order.LocationId);
        Assert.Equal(OrderType.DineIn, order.OrderType);
        Assert.Equal(Order(12.5m, 2.5m), order);
        Assert.NotEqual(Order(12.5m, 2.6m), order);
    }

    [Fact]
    public void Snapshot_equality_does_not_depend_on_line_order()
    {
        var lines = new[]
        {
            OrderLineSnapshot.Create(Pizza, Eur(9.99m)),
            OrderLineSnapshot.Create(Drink, Eur(3.33m)),
            OrderLineSnapshot.Create(Pizza, Eur(4m)),
        };

        var forward = OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, lines);
        var backward = OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, lines.Reverse());

        Assert.Equal(forward, backward);
        Assert.Equal(forward.GetHashCode(), backward.GetHashCode());
        Assert.Equal(3, backward.Lines.Count);
        Assert.NotEqual(forward, OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, lines.Take(2)));
    }

    [Fact]
    public void Snapshot_rejects_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderSnapshot.Create(default, TheOrder, Currency.EUR, Paris, OrderType.DineIn, []));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderSnapshot.Create(Tenant, default, Currency.EUR, Paris, OrderType.DineIn, []));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, default, OrderType.DineIn, []));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderLineSnapshot.Create(default, Eur(1m)));
    }

    [Fact]
    public void Snapshot_rejects_a_null_line() =>
        Assert.Throws<ArgumentNullException>(
            () => OrderSnapshot.Create(
                Tenant,
                TheOrder,
                Currency.EUR,
                Paris,
                OrderType.DineIn,
                [OrderLineSnapshot.Create(Pizza, Eur(1m)), null!]));

    [Fact]
    public void Snapshot_rejects_a_line_in_another_currency() =>
        DomainAssert.Throws(
            PromotionErrors.SnapshotLineCurrencyMismatch,
            () => OrderSnapshot.Create(
                Tenant,
                TheOrder,
                Currency.EUR,
                Paris,
                OrderType.DineIn,
                [OrderLineSnapshot.Create(Pizza, Money.Of(5m, Currency.USD))]));

    [Fact]
    public void Snapshot_rejects_a_negative_line_total() =>
        DomainAssert.Throws(
            PromotionErrors.SnapshotLineTotalNegative,
            () => OrderLineSnapshot.Create(Pizza, Eur(-0.01m)));

    [Fact]
    public void Snapshot_rejects_an_unknown_order_type() =>
        DomainAssert.Throws(
            PromotionErrors.SnapshotOrderTypeInvalid,
            () => OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, (OrderType)0, []));

    [Fact]
    public void Snapshot_carries_the_tenant_the_order_and_the_coupon_flag()
    {
        var order = Order();

        Assert.Equal((Tenant, TheOrder, false), (order.TenantId, order.OrderId, order.CouponPresented));

        var withCoupon = order.WithCouponPresented(true);
        Assert.True(withCoupon.CouponPresented);
        Assert.Equal((Tenant, TheOrder, order.Total), (withCoupon.TenantId, withCoupon.OrderId, withCoupon.Total));
        Assert.NotEqual(order, withCoupon);
        Assert.Equal(order, withCoupon.WithCouponPresented(false));
        Assert.True(OrderSnapshot.Create(Tenant, TheOrder, Currency.EUR, Paris, OrderType.DineIn, [], couponPresented: true).CouponPresented);
    }

    [Fact]
    public void Coupon_only_promotion_is_not_eligible_without_its_coupon()
    {
        var rules = Rules(Benefit.PercentageOff(10m)).WithRequiresCoupon(true);

        AssertNotEligible(DiscountIneligibility.CouponRequired, DiscountPolicy.Evaluate(Order(), rules, Now));
        Assert.Equal("promotions.discount.coupon_required", DiscountIneligibility.CouponRequired.Code);
    }

    [Fact]
    public void Coupon_only_promotion_is_eligible_when_its_coupon_is_presented()
    {
        var rules = Rules(Benefit.PercentageOff(10m)).WithRequiresCoupon(true);

        var result = DiscountPolicy.Evaluate(Order().WithCouponPresented(true), rules, Now);

        Assert.True(result.IsEligible);
        Assert.Equal(Eur(4m), result.Discount);
    }

    [Fact]
    public void Promotion_without_coupon_requirement_ignores_the_coupon_flag()
    {
        var rules = Rules(Benefit.PercentageOff(10m));

        Assert.Equal(
            DiscountPolicy.Evaluate(Order(), rules, Now).Discount,
            DiscountPolicy.Evaluate(Order().WithCouponPresented(true), rules, Now).Discount);
    }
}

namespace Promotions.Domain;

/// <summary>
/// Service de domaine pur : même commande, mêmes règles et même instant donnent toujours le même résultat. Il
/// ignore tout du statut de la promotion et de son compteur d'utilisations ; ceux-ci sont gardés par
/// <see cref="Promotion.Apply"/>.
/// </summary>
public static class DiscountPolicy
{
    public static DiscountEvaluation Evaluate(OrderSnapshot order, PromotionRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(rules);

        var nothing = Money.Zero(order.Currency);
        var conditions = rules.Conditions;

        if (rules.Benefit is null)
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.NoBenefit, nothing);
        }

        if (rules.RequiresCoupon && !order.CouponPresented)
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.CouponRequired, nothing);
        }

        if (!rules.ValidityPeriod.Contains(now))
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.OutsideValidityPeriod, nothing);
        }

        if (!conditions.AppliesToLocation(order.LocationId))
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.LocationNotEligible, nothing);
        }

        if (!conditions.AppliesToOrderType(order.OrderType))
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.OrderTypeNotEligible, nothing);
        }

        if (rules.Currency is not null && rules.Currency != order.Currency)
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.CurrencyMismatch, nothing);
        }

        if (conditions.MinimumOrderAmount is not null && order.Total < conditions.MinimumOrderAmount)
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.MinimumOrderAmountNotReached, nothing);
        }

        var eligibleTotal = order.Lines
            .Where(line => conditions.AppliesToProduct(line.ProductId))
            .Aggregate(nothing, (sum, line) => sum + line.LineTotal);

        if (!eligibleTotal.IsPositive)
        {
            return DiscountEvaluation.NotEligible(DiscountIneligibility.NoEligibleAmount, eligibleTotal);
        }

        var discount = rules.Benefit.ComputeDiscount(eligibleTotal);

        return discount.IsPositive
            ? DiscountEvaluation.Eligible(eligibleTotal, discount)
            : DiscountEvaluation.NotEligible(DiscountIneligibility.ZeroDiscount, eligibleTotal);
    }
}

namespace Promotions.Domain;

/// <summary>Motifs stables pour lesquels une commande n'obtient pas de remise. Les codes sont un contrat.</summary>
public static class DiscountIneligibility
{
    public static readonly DomainError NoBenefit = new("promotions.discount.no_benefit", "The promotion has no benefit defined.");
    public static readonly DomainError CouponRequired = new("promotions.discount.coupon_required", "The promotion only applies with one of its coupons.");
    public static readonly DomainError OutsideValidityPeriod = new("promotions.discount.outside_validity_period", "The promotion is not valid at this date.");
    public static readonly DomainError LocationNotEligible = new("promotions.discount.location_not_eligible", "The promotion does not apply to this location.");
    public static readonly DomainError OrderTypeNotEligible = new("promotions.discount.order_type_not_eligible", "The promotion does not apply to this order type.");
    public static readonly DomainError CurrencyMismatch = new("promotions.discount.currency_mismatch", "The promotion amounts are not in the order currency.");
    public static readonly DomainError MinimumOrderAmountNotReached = new("promotions.discount.minimum_order_amount_not_reached", "The order total is below the minimum order amount.");
    public static readonly DomainError NoEligibleAmount = new("promotions.discount.no_eligible_amount", "No line of the order is eligible.");
    public static readonly DomainError ZeroDiscount = new("promotions.discount.zero_discount", "The computed discount rounds to zero.");
}

/// <summary>Résultat de <see cref="DiscountPolicy.Evaluate"/>. Discount vaut zéro si non éligible.</summary>
public sealed class DiscountEvaluation : ValueObject
{
    private DiscountEvaluation(DomainError? reason, Money eligibleTotal, Money discount)
    {
        Reason = reason;
        EligibleTotal = eligibleTotal;
        Discount = discount;
    }

    public bool IsEligible => Reason is null;

    /// <summary>Pourquoi la commande n'est pas éligible ; null si elle l'est.</summary>
    public DomainError? Reason { get; }

    public string? ReasonCode => Reason?.Code;

    /// <summary>Somme des lignes auxquelles l'avantage s'applique, dans la devise de la commande.</summary>
    public Money EligibleTotal { get; }

    /// <summary>Toujours entre zéro et <see cref="EligibleTotal"/>, dans la devise de la commande.</summary>
    public Money Discount { get; }

    internal static DiscountEvaluation Eligible(Money eligibleTotal, Money discount) => new(null, eligibleTotal, discount);

    internal static DiscountEvaluation NotEligible(DomainError reason, Money eligibleTotal) =>
        new(reason, eligibleTotal, Money.Zero(eligibleTotal.Currency));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Reason?.Code;
        yield return EligibleTotal;
        yield return Discount;
    }
}

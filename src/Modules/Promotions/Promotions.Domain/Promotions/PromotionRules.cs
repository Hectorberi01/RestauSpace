namespace Promotions.Domain;

/// <summary>
/// Tout ce qui définit ce qu'une promotion accorde, quand et à qui. Un brouillon peut ne pas encore avoir d'avantage ;
/// de telles règles ne sont pas <see cref="IsComplete"/> et ne peuvent ni être activées ni produire de remise.
/// </summary>
public sealed class PromotionRules : ValueObject
{
    private PromotionRules(Period validityPeriod, Conditions conditions, Benefit? benefit, UsageLimit? usageLimit, bool requiresCoupon)
    {
        RequiresCoupon = requiresCoupon;
        ValidityPeriod = validityPeriod;
        Conditions = conditions;
        Benefit = benefit;
        UsageLimit = usageLimit;
    }

    public Period ValidityPeriod { get; }

    public Conditions Conditions { get; }

    public Benefit? Benefit { get; }

    /// <summary>Null signifie illimité.</summary>
    public UsageLimit? UsageLimit { get; }

    /// <summary>
    /// Vrai pour une promotion réservée aux coupons : elle n'est jamais appliquée automatiquement, seulement quand
    /// l'un de ses coupons est présenté avec la commande.
    /// </summary>
    public bool RequiresCoupon { get; }

    public bool IsComplete => Benefit is not null;

    /// <summary>Devise à laquelle les règles sont liées, dès qu'un montant y est exprimé.</summary>
    public Currency? Currency => Benefit?.Currency ?? Conditions.MinimumOrderAmount?.Currency;

    public static PromotionRules Create(
        Period validityPeriod,
        Conditions? conditions = null,
        Benefit? benefit = null,
        UsageLimit? usageLimit = null,
        bool requiresCoupon = false)
    {
        ArgumentNullException.ThrowIfNull(validityPeriod);
        conditions ??= Conditions.None;

        var benefitCurrency = benefit?.Currency;
        var minimumCurrency = conditions.MinimumOrderAmount?.Currency;
        DomainException.ThrowIf(
            benefitCurrency is not null && minimumCurrency is not null && benefitCurrency != minimumCurrency,
            PromotionErrors.RulesCurrencyMismatch);

        return new PromotionRules(validityPeriod, conditions, benefit, usageLimit, requiresCoupon);
    }

    public PromotionRules WithValidityPeriod(Period validityPeriod) =>
        Create(validityPeriod, Conditions, Benefit, UsageLimit, RequiresCoupon);

    public PromotionRules WithConditions(Conditions conditions) =>
        Create(ValidityPeriod, conditions, Benefit, UsageLimit, RequiresCoupon);

    public PromotionRules WithBenefit(Benefit? benefit) =>
        Create(ValidityPeriod, Conditions, benefit, UsageLimit, RequiresCoupon);

    public PromotionRules WithUsageLimit(UsageLimit? usageLimit) =>
        Create(ValidityPeriod, Conditions, Benefit, usageLimit, RequiresCoupon);

    public PromotionRules WithRequiresCoupon(bool requiresCoupon) =>
        Create(ValidityPeriod, Conditions, Benefit, UsageLimit, requiresCoupon);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ValidityPeriod;
        yield return Conditions;
        yield return Benefit;
        yield return UsageLimit;
        yield return RequiresCoupon;
    }
}

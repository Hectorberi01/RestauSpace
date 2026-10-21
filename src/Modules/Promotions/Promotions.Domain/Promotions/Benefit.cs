namespace Promotions.Domain;

/// <summary>Ce qu'obtient le client : un pourcentage (éventuellement plafonné) ou un montant fixe.</summary>
public sealed class Benefit : ValueObject
{
    private Benefit(BenefitType type, decimal? percentage, Money? amount, Money? maxDiscount)
    {
        Type = type;
        Percentage = percentage;
        Amount = amount;
        MaxDiscount = maxDiscount;
    }

    public BenefitType Type { get; }

    /// <summary>Renseigné uniquement pour <see cref="BenefitType.Percentage"/> ; 0 &lt; valeur &lt;= 100.</summary>
    public decimal? Percentage { get; }

    /// <summary>Renseigné uniquement pour <see cref="BenefitType.FixedAmount"/>.</summary>
    public Money? Amount { get; }

    /// <summary>Plafond facultatif d'un avantage en pourcentage.</summary>
    public Money? MaxDiscount { get; }

    /// <summary>Devise liée à l'avantage ; null pour un pourcentage non plafonné (valable en toute devise).</summary>
    public Currency? Currency => Amount?.Currency ?? MaxDiscount?.Currency;

    public static Benefit PercentageOff(decimal percentage, Money? maxDiscount = null)
    {
        DomainException.ThrowIf(percentage <= 0m || percentage > 100m, PromotionErrors.PercentageOutOfRange);
        DomainException.ThrowIf(maxDiscount is not null && !maxDiscount.IsPositive, PromotionErrors.MaxDiscountNotPositive);
        return new Benefit(BenefitType.Percentage, percentage, null, maxDiscount);
    }

    public static Benefit FixedAmountOff(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        DomainException.ThrowIf(!amount.IsPositive, PromotionErrors.FixedAmountNotPositive);
        return new Benefit(BenefitType.FixedAmount, null, amount, null);
    }

    /// <summary>
    /// Remise sur un total éligible de même devise : arrondie à la précision de la devise,
    /// jamais au-dessus du plafond ni au-dessus du total éligible lui-même.
    /// </summary>
    public Money ComputeDiscount(Money eligibleTotal)
    {
        ArgumentNullException.ThrowIfNull(eligibleTotal);
        if (!eligibleTotal.IsPositive)
        {
            return Money.Zero(eligibleTotal.Currency);
        }

        var discount = Type == BenefitType.Percentage
            ? Money.Rounded(eligibleTotal.Amount * Percentage!.Value / 100m, eligibleTotal.Currency)
            : Amount!;

        if (MaxDiscount is not null)
        {
            discount = Money.Min(discount, MaxDiscount);
        }

        return Money.Min(discount, eligibleTotal);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Percentage;
        yield return Amount;
        yield return MaxDiscount;
    }
}

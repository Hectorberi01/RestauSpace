namespace Orders.Domain;

/// <summary>
/// Catégorie de taxe appliquée à une ligne, figée à l'ajout de la ligne. <see cref="Rate"/> est une fraction
/// (0.20 = 20 %). Selon le <see cref="PriceTaxMode"/> de la commande, la taxe s'ajoute au montant remisé de la
/// ligne (<see cref="TaxOn"/>) ou en est extraite (<see cref="TaxIncludedIn"/>).
/// </summary>
public sealed class TaxSnapshot : ValueObject
{
    public const int MaxCategoryCodeLength = 32;
    public const int MaxCategoryNameLength = 100;
    public const int MaxRateDecimals = 6;

    private TaxSnapshot(string categoryCode, string categoryName, decimal rate)
    {
        CategoryCode = categoryCode;
        CategoryName = categoryName;
        Rate = rate;
    }

    public string CategoryCode { get; }

    public string CategoryName { get; }

    public decimal Rate { get; }

    public static TaxSnapshot Create(string? categoryCode, string? categoryName, decimal rate)
    {
        var code = categoryCode?.Trim().ToUpperInvariant();
        var name = categoryName?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(code) || code.Length > MaxCategoryCodeLength, OrderErrors.TaxCategoryCodeInvalid);
        DomainException.ThrowIf(string.IsNullOrEmpty(name) || name.Length > MaxCategoryNameLength, OrderErrors.TaxCategoryNameInvalid);
        DomainException.ThrowIf(rate is < 0m or > 1m, OrderErrors.TaxRateOutOfRange);
        DomainException.ThrowIf(decimal.Round(rate, MaxRateDecimals) != rate, OrderErrors.TaxRateTooPrecise);
        return new TaxSnapshot(code!, name!, rate);
    }

    /// <summary>Taxe due sur une base HT, arrondie au demi loin de zéro à la précision de la devise.</summary>
    public Money TaxOn(Money taxableBase)
    {
        ArgumentNullException.ThrowIfNull(taxableBase);
        return Money.Rounded(taxableBase.Amount * Rate, taxableBase.Currency);
    }

    /// <summary>
    /// Taxe contenue dans un montant TTC : brut - brut / (1 + Rate), arrondie au demi loin de zéro à la précision
    /// de la devise.
    /// </summary>
    public Money TaxIncludedIn(Money grossAmount)
    {
        ArgumentNullException.ThrowIfNull(grossAmount);
        return Money.Rounded(grossAmount.Amount - (grossAmount.Amount / (1m + Rate)), grossAmount.Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CategoryCode;
        yield return CategoryName;
        yield return Rate;
    }
}

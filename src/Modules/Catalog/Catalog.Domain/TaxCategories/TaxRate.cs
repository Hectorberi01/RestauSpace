namespace Catalog.Domain;

/// <summary>Taux de taxe en fraction décimale (0.10 = 10 %), entre 0 et 1 inclus, 6 décimales au plus.</summary>
public sealed class TaxRate : ValueObject
{
    public const int MaxDecimals = 6;

    private TaxRate(decimal value) => Value = value;

    public decimal Value { get; }

    public static TaxRate Zero => new(0m);

    public static TaxRate Create(decimal value)
    {
        DomainException.ThrowIf(value is < 0m or > 1m, TaxCategoryErrors.RateOutOfRange);
        DomainException.ThrowIf(decimal.Round(value, MaxDecimals) != value, TaxCategoryErrors.RateTooPrecise);

        return new TaxRate(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

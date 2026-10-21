namespace Catalog.Domain;

/// <summary>Nom affiché d'une catégorie de taxe, rogné. Plus court que <see cref="CatalogName"/> : 100, figé par les lignes de commande.</summary>
public sealed class TaxCategoryName : ValueObject
{
    public const int MaxLength = 100;

    private TaxCategoryName(string value) => Value = value;

    public string Value { get; }

    public static TaxCategoryName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), TaxCategoryErrors.NameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxLength, TaxCategoryErrors.NameTooLong);

        return new TaxCategoryName(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

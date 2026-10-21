namespace Catalog.Domain;

/// <summary>
/// Nom affiché d'un élément du catalogue (produit, variante, groupe de modificateurs, modificateur, menu, section).
/// Sans espaces superflus ; les règles d'unicité le comparent sans tenir compte de la casse via <see cref="Matches"/>.
/// </summary>
public sealed class CatalogName : ValueObject
{
    public const int MaxLength = 120;

    private CatalogName(string value) => Value = value;

    public string Value { get; }

    public static CatalogName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CatalogErrors.NameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxLength, CatalogErrors.NameTooLong);

        return new CatalogName(trimmed);
    }

    public bool Matches(CatalogName other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

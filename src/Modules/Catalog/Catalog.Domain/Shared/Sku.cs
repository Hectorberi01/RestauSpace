namespace Catalog.Domain;

/// <summary>Code SKU, normalisé en majuscules : deux SKU ne différant que par la casse sont le même code.</summary>
public sealed class Sku : ValueObject
{
    public const int MaxLength = 64;

    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Sku Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CatalogErrors.SkuInvalid);

        var normalized = value!.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalized.Length > MaxLength || !normalized.All(IsAllowed),
            CatalogErrors.SkuInvalid);

        return new Sku(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}

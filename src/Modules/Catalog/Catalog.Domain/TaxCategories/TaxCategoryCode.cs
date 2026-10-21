namespace Catalog.Domain;

/// <summary>
/// Code stable d'une catégorie fiscale (« VAT_STD », « TVA-10 »...) : sans espaces superflus, en majuscules, 1..32
/// caractères parmi A-Z, 0-9, '_' et '-'. C'est le format que figent les lignes de commande : ne pas l'élargir de ce
/// seul côté.
/// </summary>
public sealed class TaxCategoryCode : ValueObject
{
    public const int MaxLength = 32;

    private TaxCategoryCode(string value) => Value = value;

    public string Value { get; }

    public static TaxCategoryCode Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), TaxCategoryErrors.CodeInvalid);

        var normalized = value!.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalized.Length > MaxLength || !normalized.All(IsAllowed),
            TaxCategoryErrors.CodeInvalid);

        return new TaxCategoryCode(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    private static bool IsAllowed(char c) => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '_' or '-';
}

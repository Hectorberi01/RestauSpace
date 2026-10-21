namespace Payments.Domain;

/// <summary>
/// Pays au format ISO 3166-1 alpha-2 (« BJ », « FR »), en majuscules. Copie locale : Payments ne référence pas le
/// domaine de Locations (le pays d'un établissement arrive par son code).
/// </summary>
public sealed class CountryCode : ValueObject
{
    private CountryCode(string value) => Value = value;

    public string Value { get; }

    public static CountryCode Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalized is not { Length: 2 } || !normalized.All(character => character is >= 'A' and <= 'Z'),
            PaymentProviderErrors.CountryInvalid);
        return new CountryCode(normalized!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

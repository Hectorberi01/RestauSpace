using System.Text.RegularExpressions;

namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Numéro de téléphone au format E.164 (ex. +22997000000). Espaces, points, tirets et parenthèses
/// sont retirés.</summary>
public sealed partial class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static PhoneNumber Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CommonErrors.PhoneInvalid);

        var normalized = Separators().Replace(value!, string.Empty);
        DomainException.ThrowIf(!Pattern().IsMatch(normalized), CommonErrors.PhoneInvalid);

        return new PhoneNumber(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\+[1-9][0-9]{6,14}$")]
    private static partial Regex Pattern();
}

using System.Text.RegularExpressions;

namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Adresse email normalisée (sans espaces superflus, en minuscules).</summary>
public sealed partial class Email : ValueObject
{
    public const int MaxLength = 254;

    public const int MaxLocalPartLength = 64;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Email Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CommonErrors.EmailEmpty);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            normalized.Length > MaxLength
            || normalized.IndexOf('@') > MaxLocalPartLength
            || !Pattern().IsMatch(normalized),
            CommonErrors.EmailInvalid);

        return new Email(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9_%+\-]+(\.[a-z0-9_%+\-]+)*@[a-z0-9]([a-z0-9\-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9\-]*[a-z0-9])?)*\.[a-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

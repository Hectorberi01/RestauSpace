namespace Customers.Domain;

/// <summary>Étiquette libre d'un client, normalisée (rognée, minuscules) : "VIP" et " vip " sont la même étiquette.</summary>
public sealed class CustomerTag : ValueObject
{
    public const int MaxLength = 30;

    private CustomerTag(string value) => Value = value;

    public string Value { get; }

    public static CustomerTag Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), CustomerErrors.TagInvalid);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            normalized.Length > MaxLength || !normalized.All(IsAllowed),
            CustomerErrors.TagInvalid);

        return new CustomerTag(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    private static bool IsAllowed(char character) =>
        char.IsLetterOrDigit(character) || character is ' ' or '-' or '_';
}

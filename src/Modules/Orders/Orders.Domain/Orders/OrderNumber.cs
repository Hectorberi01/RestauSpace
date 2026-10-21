namespace Orders.Domain;

/// <summary>
/// Numéro de commande lisible, p. ex. « A-0042 » ou « 20260115-017 ». Normalisé en majuscules.
/// L'unicité par établissement ne peut pas être prouvée ici : elle relève de l'Application et de la base.
/// </summary>
public sealed class OrderNumber : ValueObject
{
    public const int MaxLength = 20;

    private OrderNumber(string value) => Value = value;

    public string Value { get; }

    public static OrderNumber Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(string.IsNullOrEmpty(normalized), OrderErrors.NumberEmpty);
        DomainException.ThrowIf(normalized!.Length > MaxLength, OrderErrors.NumberTooLong);
        DomainException.ThrowIf(!HasValidFormat(normalized), OrderErrors.NumberInvalidFormat);
        return new OrderNumber(normalized);
    }

    // Groupes de lettres/chiffres ASCII séparés par un tiret simple ; pas de tiret en tête, en fin ni doublé.
    private static bool HasValidFormat(string value)
    {
        var previousWasDash = true;
        foreach (var character in value)
        {
            if (character == '-')
            {
                if (previousWasDash)
                {
                    return false;
                }

                previousWasDash = true;
                continue;
            }

            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return false;
            }

            previousWasDash = false;
        }

        return !previousWasDash;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

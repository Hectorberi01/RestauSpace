namespace Orders.Domain;

/// <summary>Nombre d'unités d'une ligne de commande : un entier entre 1 et <see cref="MaxValue"/>.</summary>
public sealed class Quantity : ValueObject
{
    public const int MaxValue = 9_999;

    private Quantity(int value) => Value = value;

    public int Value { get; }

    public static Quantity Create(int value)
    {
        DomainException.ThrowIf(value <= 0, OrderErrors.QuantityNotPositive);
        DomainException.ThrowIf(value > MaxValue, OrderErrors.QuantityTooLarge);
        return new Quantity(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

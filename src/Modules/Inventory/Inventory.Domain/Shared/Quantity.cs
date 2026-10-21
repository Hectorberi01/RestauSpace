namespace Inventory.Domain;

/// <summary>
/// Une quantité mesurée, positive ou nulle, dans une unité. Les écarts de stock signés ne sont pas des quantités : ils
/// vivent sur <see cref="StockMovement"/> sous forme de décimal signé exprimé dans l'unité de l'article.
/// </summary>
public sealed class Quantity : ValueObject
{
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>Grain le plus fin d'une quantité : un milligramme / un microlitre exprimé en kg / l.</summary>
    public const int MaxDecimals = 6;

    private Quantity(decimal amount, UnitOfMeasure unit)
    {
        Amount = amount;
        Unit = unit;
    }

    public decimal Amount { get; }

    public UnitOfMeasure Unit { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public static Quantity Create(decimal amount, UnitOfMeasure unit)
    {
        DomainException.ThrowIf(!Enum.IsDefined(unit), InventoryErrors.UnitUnknown);
        DomainException.ThrowIf(amount < 0m, InventoryErrors.QuantityNegative);
        DomainException.ThrowIf(amount > MaxAmount, InventoryErrors.QuantityTooLarge);
        DomainException.ThrowIf(decimal.Round(amount, MaxDecimals) != amount, InventoryErrors.QuantityTooPrecise);

        return new Quantity(amount, unit);
    }

    public static Quantity Zero(UnitOfMeasure unit) => Create(0m, unit);

    public bool IsCompatibleWith(UnitOfMeasure unit) => Unit.IsCompatibleWith(unit);

    /// <summary>
    /// Conversion exacte au sein d'une même dimension ; rejetée entre dimensions. Le résultat est une quantité comme
    /// une autre : une conversion qui dépasserait <see cref="MaxAmount"/> ou <see cref="MaxDecimals"/> est rejetée.
    /// </summary>
    public Quantity ConvertTo(UnitOfMeasure target)
    {
        DomainException.ThrowIf(!Enum.IsDefined(target), InventoryErrors.UnitUnknown);
        DomainException.ThrowIf(!Unit.IsCompatibleWith(target), InventoryErrors.UnitIncompatible);

        return target == Unit ? this : Create(Amount * Unit.BaseFactor() / target.BaseFactor(), target);
    }

    public Quantity Multiply(int factor)
    {
        DomainException.ThrowIf(factor < 0, InventoryErrors.QuantityNegative);
        return Create(Amount * factor, Unit);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Unit;
    }

    public override string ToString() => $"{Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Unit}";
}

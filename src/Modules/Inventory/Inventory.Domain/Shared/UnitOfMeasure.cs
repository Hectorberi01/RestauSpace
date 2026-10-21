namespace Inventory.Domain;

public enum UnitOfMeasure
{
    Gram = 1,
    Kilogram = 2,
    Milliliter = 3,
    Liter = 4,

    /// <summary>Unité dénombrable. Les fractions sont volontairement permises (un demi-citron, un quart de gâteau) :
    /// une quantité en pièces n'est pas forcée à un entier.</summary>
    Piece = 5,
}

/// <summary>Dimension physique d'une unité. Les quantités ne se convertissent qu'au sein d'une même dimension.</summary>
public enum UnitDimension
{
    Mass = 1,
    Volume = 2,
    Count = 3,
}

public static class UnitOfMeasureExtensions
{
    public static UnitDimension Dimension(this UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Gram or UnitOfMeasure.Kilogram => UnitDimension.Mass,
        UnitOfMeasure.Milliliter or UnitOfMeasure.Liter => UnitDimension.Volume,
        UnitOfMeasure.Piece => UnitDimension.Count,
        _ => throw new DomainException(InventoryErrors.UnitUnknown),
    };

    public static bool IsCompatibleWith(this UnitOfMeasure unit, UnitOfMeasure other) => unit.Dimension() == other.Dimension();

    /// <summary>Nombre d'unités de base de la dimension (g, ml, pièce) contenues dans une unité. Toujours une puissance
    /// de dix, pour que les conversions restent exactes en decimal.</summary>
    internal static decimal BaseFactor(this UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Gram or UnitOfMeasure.Milliliter or UnitOfMeasure.Piece => 1m,
        UnitOfMeasure.Kilogram or UnitOfMeasure.Liter => 1000m,
        _ => throw new DomainException(InventoryErrors.UnitUnknown),
    };
}

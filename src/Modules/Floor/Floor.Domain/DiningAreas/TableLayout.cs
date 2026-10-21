namespace Floor.Domain;

public enum TableShape
{
    Square = 1,
    Round = 2,
    Rectangle = 3,
}

/// <summary>
/// Place et forme d'une table sur le plan dessiné de sa salle, dans une grille de <see cref="GridSize"/> × <see cref="GridSize"/>
/// cases. La table tient entièrement dans la grille ; elle peut être tournée autour de son centre, par pas de
/// <see cref="RotationStep"/> degrés.
/// </summary>
public sealed class TableLayout : ValueObject
{
    public const int GridSize = 100;
    public const int MinSize = 4;
    public const int MaxSize = 40;
    public const int RotationStep = 15;

    private TableLayout(int x, int y, int width, int height, TableShape shape, int rotation)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Shape = shape;
        Rotation = rotation;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public TableShape Shape { get; }

    /// <summary>Angle en degrés, dans le sens des aiguilles d'une montre : de 0 à 345.</summary>
    public int Rotation { get; }

    public static TableLayout Create(int x, int y, int width, int height, TableShape shape, int rotation = 0)
    {
        DomainException.ThrowIf(!Enum.IsDefined(shape), DiningAreaErrors.LayoutInvalid);
        DomainException.ThrowIf(width is < MinSize or > MaxSize || height is < MinSize or > MaxSize, DiningAreaErrors.LayoutInvalid);
        DomainException.ThrowIf(x < 0 || y < 0 || x + width > GridSize || y + height > GridSize, DiningAreaErrors.LayoutInvalid);
        DomainException.ThrowIf(shape != TableShape.Rectangle && width != height, DiningAreaErrors.LayoutInvalid);
        DomainException.ThrowIf(rotation is < 0 or >= 360 || rotation % RotationStep != 0, DiningAreaErrors.LayoutInvalid);
        return new TableLayout(x, y, width, height, shape, shape == TableShape.Round ? 0 : rotation);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return X;
        yield return Y;
        yield return Width;
        yield return Height;
        yield return Shape;
        yield return Rotation;
    }
}

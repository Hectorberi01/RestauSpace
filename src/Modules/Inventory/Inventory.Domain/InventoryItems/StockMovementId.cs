namespace Inventory.Domain;

public readonly record struct StockMovementId
{
    private StockMovementId(Guid value) => Value = value;

    public Guid Value { get; }

    public static StockMovementId New() => new(Guid.CreateVersion7());

    public static StockMovementId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new StockMovementId(value);
    }

    public override string ToString() => Value.ToString();
}

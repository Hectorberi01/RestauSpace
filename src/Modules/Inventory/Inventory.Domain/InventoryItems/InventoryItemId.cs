namespace Inventory.Domain;

public readonly record struct InventoryItemId
{
    private InventoryItemId(Guid value) => Value = value;

    public Guid Value { get; }

    public static InventoryItemId New() => new(Guid.CreateVersion7());

    public static InventoryItemId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new InventoryItemId(value);
    }

    public override string ToString() => Value.ToString();
}

namespace Inventory.Domain;

public readonly record struct IngredientId
{
    private IngredientId(Guid value) => Value = value;

    public Guid Value { get; }

    public static IngredientId New() => new(Guid.CreateVersion7());

    public static IngredientId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new IngredientId(value);
    }

    public override string ToString() => Value.ToString();
}

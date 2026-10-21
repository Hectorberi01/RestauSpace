namespace Inventory.Domain;

public readonly record struct RecipeId
{
    private RecipeId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RecipeId New() => new(Guid.CreateVersion7());

    public static RecipeId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RecipeId(value);
    }

    public override string ToString() => Value.ToString();
}

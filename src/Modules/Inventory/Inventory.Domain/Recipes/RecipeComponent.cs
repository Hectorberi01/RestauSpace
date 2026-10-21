namespace Inventory.Domain;

/// <summary>Une ligne d'ingrédient d'une recette : la quantité d'ingrédient utilisée par une unité du produit.</summary>
public sealed class RecipeComponent : ValueObject
{
    internal RecipeComponent(IngredientId ingredientId, Quantity quantity)
    {
        IngredientId = ingredientId;
        Quantity = quantity;
    }

    // Réservé à l'ORM.
    private RecipeComponent()
    {
    }

    public IngredientId IngredientId { get; private init; }

    public Quantity Quantity { get; private init; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IngredientId;
        yield return Quantity;
    }
}

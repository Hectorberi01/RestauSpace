namespace Inventory.Domain;

public enum RecipeChangeType
{
    IngredientAdded = 1,
    QuantityChanged = 2,
    IngredientRemoved = 3,
}

/// <summary>
/// <paramref name="VariantId"/> est null pour une recette au niveau du produit. <paramref name="Quantity"/> est la
/// quantité qu'a désormais l'ingrédient dans la recette (ajouté ou modifié) ; null quand l'ingrédient a été retiré.
/// </summary>
public sealed record RecipeChanged(
    TenantId TenantId,
    RecipeId RecipeId,
    ProductId ProductId,
    VariantId? VariantId,
    RecipeChangeType ChangeType,
    IngredientId IngredientId,
    Quantity? Quantity,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

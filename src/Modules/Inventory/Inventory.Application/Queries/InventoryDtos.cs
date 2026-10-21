using Inventory.Domain;

namespace Inventory.Application.Queries;

public sealed record IngredientDto(Guid Id, string Name, UnitOfMeasure BaseUnit, bool IsActive)
{
    public static IngredientDto From(Ingredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        return new IngredientDto(ingredient.Id.Value, ingredient.Name, ingredient.BaseUnit, ingredient.IsActive);
    }
}

/// <param name="IngredientName">Nom actuel de l'ingrédient (null s'il n'est plus lisible).</param>
/// <param name="CurrentQuantity">Stock courant, en <paramref name="Unit"/> ; négatif possible après une vente.</param>
/// <param name="IsLowStock">Stock au seuil ou en dessous.</param>
public sealed record InventoryItemDto(
    Guid Id,
    Guid LocationId,
    Guid IngredientId,
    string? IngredientName,
    UnitOfMeasure Unit,
    decimal CurrentQuantity,
    decimal ReorderThreshold,
    NegativeStockPolicy NegativeStockPolicy,
    bool IsLowStock,
    decimal? AverageUnitCost = null)
{
    public static InventoryItemDto From(InventoryItem item, string? ingredientName)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryItemDto(
            item.Id.Value,
            item.LocationId.Value,
            item.IngredientId.Value,
            ingredientName,
            item.Unit,
            item.CurrentQuantity,
            item.ReorderThreshold.Amount,
            item.NegativeStockPolicy,
            item.IsLowStock,
            item.AverageUnitCost);
    }
}

/// <param name="QuantityDelta">Signé, dans l'unité de l'article : positif = entrée, négatif = sortie.</param>
public sealed record StockMovementDto(
    Guid Id, StockMovementType Type, decimal QuantityDelta, UnitOfMeasure Unit, DateTimeOffset OccurredAtUtc, string? Reference, string? Reason,
    decimal? UnitCost = null)
{
    public static StockMovementDto From(StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return new StockMovementDto(
            movement.Id.Value, movement.Type, movement.QuantityDelta, movement.Unit, movement.OccurredAtUtc, movement.Reference, movement.Reason,
            movement.UnitCost);
    }
}

/// <param name="Quantity">Par unité vendue du produit.</param>
public sealed record RecipeIngredientDto(Guid IngredientId, string? IngredientName, decimal Quantity, UnitOfMeasure Unit);

/// <param name="VariantId">Null : recette au niveau produit.</param>
public sealed record RecipeDto(Guid Id, Guid ProductId, Guid? VariantId, IReadOnlyList<RecipeIngredientDto> Ingredients)
{
    public static RecipeDto From(Recipe recipe, IReadOnlyDictionary<IngredientId, string> names)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(names);
        return new RecipeDto(
            recipe.Id.Value,
            recipe.ProductId.Value,
            recipe.VariantId?.Value,
            recipe.Ingredients
                .Select(component => new RecipeIngredientDto(
                    component.IngredientId.Value,
                    names.GetValueOrDefault(component.IngredientId),
                    component.Quantity.Amount,
                    component.Quantity.Unit))
                .ToList());
    }
}

namespace Inventory.Domain;

/// <summary>Port : un seul article de stock par (établissement, ingrédient).</summary>
public interface IInventoryItemUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, LocationId locationId, IngredientId ingredientId, CancellationToken cancellationToken = default);
}

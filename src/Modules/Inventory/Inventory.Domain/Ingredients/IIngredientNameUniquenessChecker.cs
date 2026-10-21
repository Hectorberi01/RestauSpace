namespace Inventory.Domain;

/// <summary>Port : indique si un nom d'ingrédient (comparé sans casse) est libre dans un tenant.</summary>
public interface IIngredientNameUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, string name, IngredientId? excludedIngredientId, CancellationToken cancellationToken = default);
}

namespace Inventory.Domain;

/// <summary>
/// Port : une recette par (produit, variante) dans un tenant. Une variante null désigne la recette au niveau produit,
/// elle-même unique ; l'index unique en base reste le garde-fou final.
/// </summary>
public interface IRecipeUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, ProductId productId, VariantId? variantId, CancellationToken cancellationToken = default);
}

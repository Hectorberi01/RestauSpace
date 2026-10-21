namespace Inventory.Domain;

/// <summary>
/// Le produit pour lequel une recette est écrite, tel que chargé depuis le module Catalog sous isolation de tenant.
/// Construit par l'Application (agrégat d'un autre module) : ses ids sont vérifiés ici et ne peuvent plus changer.
/// </summary>
public sealed record ProductReference(TenantId TenantId, ProductId ProductId)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public ProductId ProductId { get; } = Guard.NotEmpty(ProductId);
}

namespace Catalog.Domain;

/// <summary>Ce dont un menu a besoin pour lister un produit. Obtenu uniquement via <see cref="Product.ToReference"/>.</summary>
public sealed record ProductReference
{
    internal ProductReference(ProductId id, TenantId tenantId, bool isArchived)
    {
        Id = Guard.NotEmpty(id);
        TenantId = Guard.NotEmpty(tenantId);
        IsArchived = isArchived;
    }

    public ProductId Id { get; }

    public TenantId TenantId { get; }

    public bool IsArchived { get; }
}

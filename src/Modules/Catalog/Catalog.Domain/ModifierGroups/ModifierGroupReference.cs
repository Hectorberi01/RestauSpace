namespace Catalog.Domain;

/// <summary>Ce qu'un produit doit savoir d'un groupe de modificateurs pour s'y rattacher. Obtenable uniquement via
/// <see cref="ModifierGroup.ToReference"/>.</summary>
public sealed record ModifierGroupReference
{
    internal ModifierGroupReference(ModifierGroupId id, TenantId tenantId, Currency currency)
    {
        Id = Guard.NotEmpty(id);
        TenantId = Guard.NotEmpty(tenantId);
        Currency = currency;
    }

    public ModifierGroupId Id { get; }

    public TenantId TenantId { get; }

    public Currency Currency { get; }
}

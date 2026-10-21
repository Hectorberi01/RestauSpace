namespace Catalog.Domain;

/// <summary>
/// Ce qu'un produit doit savoir d'une catégorie fiscale pour l'utiliser, et ce que l'Application copie dans une
/// ligne de commande (code, nom, taux). Obtenable uniquement via <see cref="TaxCategory.ToReference"/>.
/// </summary>
public sealed record TaxCategoryReference
{
    internal TaxCategoryReference(TaxCategoryId id, TenantId tenantId, string code, string name, decimal rate, bool isArchived)
    {
        Id = Guard.NotEmpty(id);
        TenantId = Guard.NotEmpty(tenantId);
        Code = code;
        Name = name;
        Rate = rate;
        IsArchived = isArchived;
    }

    public TaxCategoryId Id { get; }

    public TenantId TenantId { get; }

    public string Code { get; }

    public string Name { get; }

    public decimal Rate { get; }

    public bool IsArchived { get; }
}

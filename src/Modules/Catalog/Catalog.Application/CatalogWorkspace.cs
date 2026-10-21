using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on sauvegarde.
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class CatalogWorkspace(
    ITenantContext tenantContext,
    ICatalogTenants tenants,
    IProductRepository products,
    IModifierGroupRepository modifierGroups,
    IMenuRepository menus,
    ITaxCategoryRepository taxCategories,
    ICatalogUnitOfWork unitOfWork)
{
    public IProductRepository Products => products;

    public IModifierGroupRepository ModifierGroups => modifierGroups;

    public IMenuRepository Menus => menus;

    public ITaxCategoryRepository TaxCategories => taxCategories;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Le tenant courant, refusé s'il est suspendu, fermé ou inconnu.</summary>
    public async Task<CatalogTenant> RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        var tenant = await tenants.FindAsync(CurrentTenant, cancellationToken);
        if (tenant is not { AcceptsBusinessWrites: true })
        {
            throw new DomainException(CatalogApplicationErrors.TenantNotWritable);
        }

        return tenant;
    }

    public async Task<Product> LoadProductAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await products.FindAsync(tenantId, ProductId.From(id), cancellationToken))
        ?? throw new NotFoundException(CatalogApplicationErrors.ProductNotFound);

    public async Task<ModifierGroup> LoadModifierGroupAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await modifierGroups.FindAsync(tenantId, ModifierGroupId.From(id), cancellationToken))
        ?? throw new NotFoundException(CatalogApplicationErrors.ModifierGroupNotFound);

    public async Task<Menu> LoadMenuAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await menus.FindAsync(tenantId, MenuId.From(id), cancellationToken))
        ?? throw new NotFoundException(CatalogApplicationErrors.MenuNotFound);

    public async Task<TaxCategory> LoadTaxCategoryAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await taxCategories.FindAsync(tenantId, TaxCategoryId.From(id), cancellationToken))
        ?? throw new NotFoundException(CatalogApplicationErrors.TaxCategoryNotFound);

    /// <summary>Charge le produit du tenant courant, applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditProductAsync(Guid id, Action<Product> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadProductAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public async Task EditModifierGroupAsync(Guid id, Action<ModifierGroup> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadModifierGroupAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public async Task EditMenuAsync(Guid id, Action<Menu> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadMenuAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public async Task EditTaxCategoryAsync(Guid id, Action<TaxCategory> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadTaxCategoryAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

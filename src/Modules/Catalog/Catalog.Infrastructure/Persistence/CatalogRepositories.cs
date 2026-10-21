using Catalog.Application.Abstractions;
using Catalog.Domain;
using Catalog.Infrastructure.Persistence.Configurations;

namespace Catalog.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.
// Les enfants ordonnés (variantes, options, sections) sont toujours chargés dans l'ordre de leur position.

internal sealed class ProductRepository(CatalogDbContext context) : IProductRepository
{
    private IQueryable<Product> Products => context.Products
        .Include(product => product.Variants.OrderBy(variant => EF.Property<int>(variant, Ordering.PositionProperty)));

    public Task<Product?> FindAsync(TenantId tenantId, ProductId id, CancellationToken cancellationToken = default) =>
        Products.SingleOrDefaultAsync(product => product.TenantId == tenantId && product.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> FindManyAsync(
        TenantId tenantId, IEnumerable<ProductId> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await Products
            .Where(product => product.TenantId == tenantId && wanted.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductPage> ListAsync(
        TenantId tenantId, ProductStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = Products.AsNoTracking().Where(product => product.TenantId == tenantId);
        query = status is { } wanted
            ? query.Where(product => product.Status == wanted)
            : query.Where(product => product.Status != ProductStatus.Archived);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new ProductPage(items, total);
    }

    public void Add(Product product) => context.Products.Add(product);
}

internal sealed class ModifierGroupRepository(CatalogDbContext context) : IModifierGroupRepository
{
    private IQueryable<ModifierGroup> Groups => context.ModifierGroups
        .Include(group => group.Modifiers.OrderBy(modifier => EF.Property<int>(modifier, Ordering.PositionProperty)));

    public Task<ModifierGroup?> FindAsync(TenantId tenantId, ModifierGroupId id, CancellationToken cancellationToken = default) =>
        Groups.SingleOrDefaultAsync(group => group.TenantId == tenantId && group.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ModifierGroup>> FindManyAsync(
        TenantId tenantId, IEnumerable<ModifierGroupId> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await Groups
            .Where(group => group.TenantId == tenantId && wanted.Contains(group.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModifierGroup>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await Groups.AsNoTracking()
            .Where(group => group.TenantId == tenantId)
            .OrderBy(group => group.Name)
            .ThenBy(group => group.Id)
            .ToListAsync(cancellationToken);

    public void Add(ModifierGroup group) => context.ModifierGroups.Add(group);
}

internal sealed class MenuRepository(CatalogDbContext context) : IMenuRepository
{
    private IQueryable<Menu> Menus => context.Menus
        .Include(menu => menu.Sections.OrderBy(section => EF.Property<int>(section, Ordering.PositionProperty)));

    public Task<Menu?> FindAsync(TenantId tenantId, MenuId id, CancellationToken cancellationToken = default) =>
        Menus.SingleOrDefaultAsync(menu => menu.TenantId == tenantId && menu.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Menu>> ListAsync(
        TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default)
    {
        var query = Menus.AsNoTracking().Where(menu => menu.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(menu => menu.LocationId == location);
        }

        return await query.OrderBy(menu => menu.Name).ThenBy(menu => menu.Id).ToListAsync(cancellationToken);
    }

    public void Add(Menu menu) => context.Menus.Add(menu);
}

internal sealed class TaxCategoryRepository(CatalogDbContext context) : ITaxCategoryRepository
{
    public Task<TaxCategory?> FindAsync(TenantId tenantId, TaxCategoryId id, CancellationToken cancellationToken = default) =>
        context.TaxCategories.SingleOrDefaultAsync(category => category.TenantId == tenantId && category.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaxCategory>> ListAsync(
        TenantId tenantId, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = context.TaxCategories.AsNoTracking().Where(category => category.TenantId == tenantId);
        if (!includeArchived)
        {
            query = query.Where(category => category.Status != TaxCategoryStatus.Archived);
        }

        return await query.OrderBy(category => category.Code).ToListAsync(cancellationToken);
    }

    public void Add(TaxCategory category) => context.TaxCategories.Add(category);
}

internal sealed class TaxCategoryCodeUniquenessChecker(CatalogDbContext context) : ITaxCategoryCodeUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(TenantId tenantId, TaxCategoryCode code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        return !await context.TaxCategories
            .AnyAsync(category => category.TenantId == tenantId && category.Code == code, cancellationToken);
    }
}

/// <summary>
/// Appelé HORS requête de tenant (route plateforme du module Tenants) : le query filter masquerait tout, il est donc
/// contourné ICI SEULEMENT, et remplacé par le tenant explicite.
/// </summary>
internal sealed class PricedCatalogLookup(CatalogDbContext context) : IPricedCatalogLookup
{
    public async Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Products.IgnoreQueryFilters().AnyAsync(product => product.TenantId == tenantId, cancellationToken)
        || await context.ModifierGroups.IgnoreQueryFilters().AnyAsync(group => group.TenantId == tenantId, cancellationToken);
}

/// <summary>
/// Menu public : appelé HORS requête de tenant, le query filter masquerait tout ; il est contourné ICI SEULEMENT et
/// remplacé par le tenant explicite. Seuls les menus publiés de l'établissement sont lus.
/// </summary>
internal sealed class PublicMenuLookup(CatalogDbContext context) : IPublicMenuLookup
{
    public async Task<IReadOnlyList<Menu>> PublishedMenusAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await context.Menus.IgnoreQueryFilters().AsNoTracking()
            .Include(menu => menu.Sections.OrderBy(section => EF.Property<int>(section, Ordering.PositionProperty)))
            .Where(menu => menu.TenantId == tenantId && menu.LocationId == locationId && menu.Status == MenuStatus.Published)
            .OrderBy(menu => menu.Name)
            .ThenBy(menu => menu.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> ProductsAsync(
        TenantId tenantId, IReadOnlyCollection<ProductId> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var wanted = ids.ToList();
        return await context.Products.IgnoreQueryFilters().AsNoTracking()
            .Include(product => product.Variants.OrderBy(variant => EF.Property<int>(variant, Ordering.PositionProperty)))
            .Where(product => product.TenantId == tenantId && wanted.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }
}


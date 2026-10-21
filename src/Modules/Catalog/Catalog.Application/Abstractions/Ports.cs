using Catalog.Domain;

namespace Catalog.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IProductRepository
{
    Task<Product?> FindAsync(TenantId tenantId, ProductId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> FindManyAsync(TenantId tenantId, IEnumerable<ProductId> ids, CancellationToken cancellationToken = default);

    /// <summary>Page en lecture seule, triée par nom ; <paramref name="status"/> null = tous sauf archivés.</summary>
    Task<ProductPage> ListAsync(
        TenantId tenantId, ProductStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    void Add(Product product);
}

public sealed record ProductPage(IReadOnlyList<Product> Items, int TotalCount);

public interface IModifierGroupRepository
{
    Task<ModifierGroup?> FindAsync(TenantId tenantId, ModifierGroupId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModifierGroup>> FindManyAsync(
        TenantId tenantId, IEnumerable<ModifierGroupId> ids, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, triée par nom.</summary>
    Task<IReadOnlyList<ModifierGroup>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(ModifierGroup group);
}

public interface IMenuRepository
{
    Task<Menu?> FindAsync(TenantId tenantId, MenuId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, triée par nom ; <paramref name="locationId"/> null = tous les établissements.</summary>
    Task<IReadOnlyList<Menu>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default);

    void Add(Menu menu);
}

public interface ITaxCategoryRepository
{
    Task<TaxCategory?> FindAsync(TenantId tenantId, TaxCategoryId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, triée par code.</summary>
    Task<IReadOnlyList<TaxCategory>> ListAsync(TenantId tenantId, bool includeArchived, CancellationToken cancellationToken = default);

    void Add(TaxCategory category);
}

/// <summary>
/// Pour le module Tenants (changement de devise) : le tenant a-t-il déjà un prix dans son catalogue (produit ou groupe
/// d'options) ? Appelé HORS requête tenant (route plateforme) : le tenant est donné explicitement.
/// </summary>
public interface IPricedCatalogLookup
{
    Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Menu public (QR code de table) : appelé HORS requête de tenant ; le tenant est donné explicitement par l'hôte, qui
/// l'a lu sur l'établissement (jamais dans la requête). Lecture seule.
/// </summary>
public interface IPublicMenuLookup
{
    Task<IReadOnlyList<Menu>> PublishedMenusAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> ProductsAsync(TenantId tenantId, IReadOnlyCollection<ProductId> ids, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Catalog doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface ICatalogTenants
{
    /// <summary>Null quand le tenant n'existe pas.</summary>
    Task<CatalogTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <param name="DefaultCurrency">Devise des nouveaux produits et groupes d'options.</param>
/// <param name="AcceptsBusinessWrites">Faux pour un tenant suspendu ou fermé.</param>
public sealed record CatalogTenant(Currency DefaultCurrency, bool AcceptsBusinessWrites);

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis le module Locations ; null s'il n'existe pas.</summary>
public interface ICatalogLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

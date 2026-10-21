using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une ressource d'un
// autre tenant n'est jamais trouvée.

public sealed record GetTaxCategoryByIdQuery(Guid TaxCategoryId) : IQuery<TaxCategoryDto>;

public sealed class GetTaxCategoryByIdHandler(CatalogWorkspace workspace) : IQueryHandler<GetTaxCategoryByIdQuery, TaxCategoryDto>
{
    public async Task<TaxCategoryDto> HandleAsync(GetTaxCategoryByIdQuery query, CancellationToken cancellationToken = default) =>
        TaxCategoryDto.From(await workspace.LoadTaxCategoryAsync(workspace.CurrentTenant, query.TaxCategoryId, cancellationToken));
}

/// <summary>Par code ; les archivées seulement sur demande.</summary>
public sealed record ListTaxCategoriesQuery(bool IncludeArchived = false) : IQuery<IReadOnlyList<TaxCategoryDto>>;

public sealed class ListTaxCategoriesHandler(CatalogWorkspace workspace)
    : IQueryHandler<ListTaxCategoriesQuery, IReadOnlyList<TaxCategoryDto>>
{
    public async Task<IReadOnlyList<TaxCategoryDto>> HandleAsync(
        ListTaxCategoriesQuery query, CancellationToken cancellationToken = default)
    {
        var categories = await workspace.TaxCategories.ListAsync(workspace.CurrentTenant, query.IncludeArchived, cancellationToken);
        return categories.Select(TaxCategoryDto.From).ToList();
    }
}

public sealed record GetModifierGroupByIdQuery(Guid ModifierGroupId) : IQuery<ModifierGroupDto>;

public sealed class GetModifierGroupByIdHandler(CatalogWorkspace workspace) : IQueryHandler<GetModifierGroupByIdQuery, ModifierGroupDto>
{
    public async Task<ModifierGroupDto> HandleAsync(GetModifierGroupByIdQuery query, CancellationToken cancellationToken = default) =>
        ModifierGroupDto.From(await workspace.LoadModifierGroupAsync(workspace.CurrentTenant, query.ModifierGroupId, cancellationToken));
}

/// <summary>Par nom.</summary>
public sealed record ListModifierGroupsQuery : IQuery<IReadOnlyList<ModifierGroupDto>>;

public sealed class ListModifierGroupsHandler(CatalogWorkspace workspace)
    : IQueryHandler<ListModifierGroupsQuery, IReadOnlyList<ModifierGroupDto>>
{
    public async Task<IReadOnlyList<ModifierGroupDto>> HandleAsync(
        ListModifierGroupsQuery query, CancellationToken cancellationToken = default)
    {
        var groups = await workspace.ModifierGroups.ListAsync(workspace.CurrentTenant, cancellationToken);
        return groups.Select(ModifierGroupDto.From).ToList();
    }
}

public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;

public sealed class GetProductByIdHandler(CatalogWorkspace workspace) : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default) =>
        ProductDto.From(await workspace.LoadProductAsync(workspace.CurrentTenant, query.ProductId, cancellationToken));
}

/// <summary>Produits par nom. <paramref name="Status"/> null = tous sauf les archivés.</summary>
public sealed record ListProductsQuery(int Page = 1, int PageSize = ListProductsQuery.DefaultPageSize, ProductStatus? Status = null)
    : IQuery<PagedResult<ProductSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Garde l'offset loin du dépassement de capacité.</summary>
    public const int MaxPage = 100_000;
}

public sealed class ListProductsHandler(CatalogWorkspace workspace) : IQueryHandler<ListProductsQuery, PagedResult<ProductSummaryDto>>
{
    public async Task<PagedResult<ProductSummaryDto>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page is < 1 or > ListProductsQuery.MaxPage || query.PageSize is < 1 or > ListProductsQuery.MaxPageSize)
        {
            throw new DomainException(CatalogApplicationErrors.PagingInvalid);
        }

        var page = await workspace.Products.ListAsync(
            workspace.CurrentTenant, query.Status, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        return PagedResult<ProductSummaryDto>.Counted(page.Items.Select(ProductSummaryDto.From).ToList(), query.Page, query.PageSize, page.TotalCount);
    }
}

public sealed record GetMenuByIdQuery(Guid MenuId) : IQuery<MenuDto>;

public sealed class GetMenuByIdHandler(CatalogWorkspace workspace) : IQueryHandler<GetMenuByIdQuery, MenuDto>
{
    public async Task<MenuDto> HandleAsync(GetMenuByIdQuery query, CancellationToken cancellationToken = default) =>
        MenuDto.From(await workspace.LoadMenuAsync(workspace.CurrentTenant, query.MenuId, cancellationToken));
}

/// <summary>Menus par nom, archivés compris ; <paramref name="LocationId"/> null = tous les établissements.</summary>
public sealed record ListMenusQuery(Guid? LocationId = null) : IQuery<IReadOnlyList<MenuSummaryDto>>;

public sealed class ListMenusHandler(CatalogWorkspace workspace) : IQueryHandler<ListMenusQuery, IReadOnlyList<MenuSummaryDto>>
{
    public async Task<IReadOnlyList<MenuSummaryDto>> HandleAsync(ListMenusQuery query, CancellationToken cancellationToken = default)
    {
        // Un id vide ne désigne aucun établissement : liste vide plutôt qu'erreur, comme un id inconnu.
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var location = query.LocationId is { } id ? LocationId.From(id) : (LocationId?)null;
        var menus = await workspace.Menus.ListAsync(workspace.CurrentTenant, location, cancellationToken);
        return menus.Select(MenuSummaryDto.From).ToList();
    }
}

/// <summary>
/// Pour le module Tenants, via l'hôte : le tenant donné a-t-il déjà un prix au catalogue (produit ou groupe d'options,
/// archivés compris) ? Route plateforme : le tenant est un paramètre, pas celui de la requête.
/// </summary>
public sealed record HasPricedCatalogQuery(Guid TenantId) : IQuery<bool>;

public sealed class HasPricedCatalogHandler(IPricedCatalogLookup lookup) : IQueryHandler<HasPricedCatalogQuery, bool>
{
    public Task<bool> HandleAsync(HasPricedCatalogQuery query, CancellationToken cancellationToken = default) =>
        query.TenantId == Guid.Empty
            ? Task.FromResult(false)
            : lookup.HasPricedCatalogAsync(TenantId.From(query.TenantId), cancellationToken);
}

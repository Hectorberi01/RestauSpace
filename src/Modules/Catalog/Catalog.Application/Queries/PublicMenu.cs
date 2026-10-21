using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.Queries;

public sealed record PublicMenuVariantDto(string Name, decimal Price, bool IsAvailable);

/// <param name="Price">Prix de base, dans le mode de prix du restaurant (TTC ou HT).</param>
public sealed record PublicMenuItemDto(string Name, string? Description, decimal Price, string CurrencyCode, bool IsAvailable, IReadOnlyList<PublicMenuVariantDto> Variants);

public sealed record PublicMenuSectionDto(string Name, IReadOnlyList<PublicMenuItemDto> Items);

/// <summary>Un menu tel qu'un client le voit : noms, descriptions et prix. Aucun id ni donnée interne.</summary>
public sealed record PublicMenuDto(string Name, IReadOnlyList<PublicMenuSectionDto> Sections);

/// <summary>
/// Les menus publiés d'un établissement, pour la page publique ouverte par le QR code d'une table. Le tenant est
/// celui de l'établissement, résolu par l'hôte : jamais une valeur fournie par le visiteur. Les produits en brouillon
/// ou archivés n'apparaissent pas ; un produit indisponible est montré comme tel.
/// </summary>
public sealed record GetPublicMenuQuery(Guid TenantId, Guid LocationId) : IQuery<IReadOnlyList<PublicMenuDto>>;

public sealed class GetPublicMenuHandler(IPublicMenuLookup lookup) : IQueryHandler<GetPublicMenuQuery, IReadOnlyList<PublicMenuDto>>
{
    public async Task<IReadOnlyList<PublicMenuDto>> HandleAsync(GetPublicMenuQuery query, CancellationToken cancellationToken = default)
    {
        if (query.TenantId == Guid.Empty || query.LocationId == Guid.Empty)
        {
            return [];
        }

        var tenant = TenantId.From(query.TenantId);
        var location = LocationId.From(query.LocationId);
        var menus = await lookup.PublishedMenusAsync(tenant, location, cancellationToken);
        var ids = menus.SelectMany(menu => menu.Sections).SelectMany(section => section.ProductIds).ToHashSet();
        var products = (await lookup.ProductsAsync(tenant, ids, cancellationToken))
            .Where(product => (product.Status is ProductStatus.Active or ProductStatus.Unavailable) && product.IsSoldAt(location))
            .ToDictionary(product => product.Id);

        return menus
            .Select(menu => new PublicMenuDto(
                menu.Name.Value,
                menu.Sections
                    .Select(section => new PublicMenuSectionDto(
                        section.Name.Value,
                        section.ProductIds
                            .Where(products.ContainsKey)
                            .Select(id => Item(products[id], location))
                            .ToList()))
                    .Where(section => section.Items.Count > 0)
                    .ToList()))
            .Where(menu => menu.Sections.Count > 0)
            .ToList();
    }

    private static PublicMenuItemDto Item(Product product, LocationId location) =>
        new(
            product.Name.Value,
            product.Description,
            product.BasePrice.Amount,
            product.BasePrice.Currency.Code,
            product.IsSellableAt(location),
            product.Variants
                .Select(variant => new PublicMenuVariantDto(variant.Name.Value, (product.BasePrice + variant.PriceDelta).Amount, product.IsSellableAt(location) && variant.IsAvailable))
                .ToList());
}

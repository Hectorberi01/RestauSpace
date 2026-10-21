using System.Security.Cryptography;
using System.Text.Json;
using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Queries;

// Montants dans la devise du restaurant (CurrencyCode), dans son mode de prix (PricesIncludeTax).

public sealed record StorefrontModifierDto(Guid Id, string Name, decimal PriceDelta, bool IsAvailable);

/// <param name="MinSelections">Nombre minimal d'options à choisir (0 = facultatif).</param>
/// <param name="MaxSelections">Nombre maximal d'options à choisir.</param>
public sealed record StorefrontModifierGroupDto(Guid Id, string Name, int MinSelections, int MaxSelections, IReadOnlyList<StorefrontModifierDto> Modifiers);

/// <param name="Price">Prix de vente de la variante.</param>
public sealed record StorefrontVariantDto(Guid Id, string Name, decimal Price, bool IsAvailable);

/// <param name="Price">Prix de base ; un produit à variantes se commande par l'une d'elles.</param>
/// <param name="IsAvailable">Commandable maintenant.</param>
/// <param name="ModifierGroupIds">Groupes d'options du produit, décrits une fois dans <c>ModifierGroups</c>.</param>
/// <param name="Allergens">Codes des 14 allergènes UE : gluten, crustaceans, eggs, fish, peanuts, soybeans, milk, nuts, celery, mustard, sesame, sulphites, lupin, molluscs.</param>
/// <param name="Images">Liens publics stables (mis en cache un an), la photo principale en premier.</param>
/// <param name="Thumbnails">La miniature de chaque photo, dans le même ordre (null si elle n'en a pas) : pour les listes.</param>
/// <summary>
/// Un produit proposé à une étape de formule : à renvoyer avec l'id de l'étape (<c>components</c> de la ligne de panier),
/// avec ses options (groupes décrits dans <c>ModifierGroups</c>). <c>Supplement</c> s'ajoute au prix de la formule.
/// </summary>
public sealed record StorefrontFormulaChoiceDto(
    Guid ProductId, Guid? VariantId, string Name, string? VariantName, decimal Supplement, bool IsAvailable, IReadOnlyList<Guid> ModifierGroupIds,
    IReadOnlyList<string> Allergens);

/// <summary>Une étape de formule (« Plat », « Boisson ») : de <c>MinChoices</c> (0 = facultative) à <c>MaxChoices</c> produits.</summary>
public sealed record StorefrontFormulaStepDto(Guid Id, string Name, int MinChoices, int MaxChoices, IReadOnlyList<StorefrontFormulaChoiceDto> Choices);

/// <param name="FormulaSteps">Formule (menu composé) : les étapes à composer ; vide pour un produit ordinaire.</param>
public sealed record StorefrontProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    bool IsAvailable,
    IReadOnlyList<StorefrontVariantDto> Variants,
    IReadOnlyList<Guid> ModifierGroupIds,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<Uri> Images,
    IReadOnlyList<Uri?>? Thumbnails = null,
    IReadOnlyList<StorefrontFormulaStepDto>? FormulaSteps = null);

/// <param name="ProductIds">Dans l'ordre d'affichage ; les produits sont décrits une fois dans <c>Products</c>.</param>
public sealed record StorefrontSectionDto(Guid Id, string Name, IReadOnlyList<Guid> ProductIds);

public sealed record StorefrontMenuDto(Guid Id, string Name, IReadOnlyList<StorefrontSectionDto> Sections);

/// <summary>Le catalogue de l'établissement pour le canal de l'application, fourni par l'hôte depuis Catalog et Files.</summary>
public sealed record StorefrontCatalogContent(
    IReadOnlyList<StorefrontMenuDto> Menus,
    IReadOnlyList<StorefrontProductDto> Products,
    IReadOnlyList<StorefrontModifierGroupDto> ModifierGroups);

/// <summary>
/// Le catalogue commandable : menus et sections, chaque produit et groupe d'options une seule fois, avec les ids qu'une
/// commande renverra. <c>Version</c> change dès que le contenu change : c'est aussi l'ETag de la réponse.
/// </summary>
public sealed record StorefrontCatalogDto(
    Guid LocationId,
    string CurrencyCode,
    bool PricesIncludeTax,
    IReadOnlyList<StorefrontMenuDto> Menus,
    IReadOnlyList<StorefrontProductDto> Products,
    IReadOnlyList<StorefrontModifierGroupDto> ModifierGroups,
    string Version);

/// <summary>Application de vente : le catalogue d'un établissement qu'elle sert (404 sinon), pour son canal.</summary>
public sealed record GetStorefrontCatalogQuery(Guid LocationId) : IQuery<StorefrontCatalogDto>;

public sealed class GetStorefrontCatalogHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontCatalog catalog, IClock clock)
    : IQueryHandler<GetStorefrontCatalogQuery, StorefrontCatalogDto>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StorefrontCatalogDto> HandleAsync(GetStorefrontCatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (app, restaurant, _) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        var served = (await workspace.Locations.ListAsync(workspace.CurrentTenant, clock.UtcNow, cancellationToken))
            .Any(location => location.LocationId.Value == query.LocationId && StorefrontReader.Serves(app, client, location.LocationId));
        if (!served)
        {
            throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        }

        var content = await catalog.GetAsync(workspace.CurrentTenant, LocationId.From(query.LocationId), app.Kind, cancellationToken);
        var unversioned = new StorefrontCatalogDto(
            query.LocationId, restaurant.CurrencyCode, restaurant.PricesIncludeTax, content.Menus, content.Products, content.ModifierGroups, string.Empty);
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(unversioned, Json));
        return unversioned with { Version = Convert.ToHexStringLower(hash.AsSpan(0, 16)) };
    }
}

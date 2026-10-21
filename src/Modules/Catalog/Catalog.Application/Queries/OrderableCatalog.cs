using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.Queries;

// Les montants sont dans la devise du catalogue (CurrencyCode), dans le mode de prix du restaurant (TTC ou HT).

public sealed record OrderableModifierDto(Guid Id, string Name, decimal PriceDelta, bool IsAvailable);

/// <param name="MinSelections">Nombre minimal d'options à choisir (0 = facultatif).</param>
/// <param name="MaxSelections">Nombre maximal d'options à choisir.</param>
public sealed record OrderableModifierGroupDto(Guid Id, string Name, int MinSelections, int MaxSelections, IReadOnlyList<OrderableModifierDto> Modifiers);

/// <param name="Price">Prix de vente de la variante (prix de base + delta).</param>
public sealed record OrderableVariantDto(Guid Id, string Name, decimal Price, bool IsAvailable);

/// <summary>
/// Un produit proposé à une étape de formule : son nom (et sa variante), le supplément ajouté au prix de la formule, et
/// ses groupes d'options (décrits dans <c>ModifierGroups</c>).
/// </summary>
public sealed record OrderableFormulaChoiceDto(
    Guid ProductId,
    Guid? VariantId,
    string Name,
    string? VariantName,
    decimal Supplement,
    bool IsAvailable,
    IReadOnlyList<Guid> ModifierGroupIds,
    IReadOnlyList<string> Allergens);

/// <param name="MinChoices">0 : étape facultative.</param>
public sealed record OrderableFormulaStepDto(Guid Id, string Name, int MinChoices, int MaxChoices, IReadOnlyList<OrderableFormulaChoiceDto> Choices);

/// <param name="Price">Prix de base ; un produit à variantes se commande par l'une d'elles.</param>
/// <param name="IsAvailable">Commandable maintenant : actif, une variante disponible s'il en a, et chaque groupe d'options obligatoire satisfaisable.</param>
/// <param name="ModifierGroupIds">Dans l'ordre d'affichage ; les groupes sont décrits une fois dans <c>ModifierGroups</c>.</param>
/// <param name="Allergens">Codes du référentiel des 14 allergènes UE.</param>
public sealed record OrderableProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    bool IsAvailable,
    IReadOnlyList<OrderableVariantDto> Variants,
    IReadOnlyList<Guid> ModifierGroupIds,
    IReadOnlyList<string> Allergens,
    IReadOnlyList<OrderableFormulaStepDto>? FormulaSteps = null)
{
    public bool IsFormula => FormulaSteps is { Count: > 0 };
}

/// <param name="ProductIds">Dans l'ordre d'affichage ; les produits sont décrits une fois dans <c>Products</c>.</param>
public sealed record OrderableSectionDto(Guid Id, string Name, IReadOnlyList<Guid> ProductIds);

public sealed record OrderableMenuDto(Guid Id, string Name, IReadOnlyList<OrderableSectionDto> Sections);

/// <summary>
/// Le catalogue commandable d'un établissement pour un canal : menus publiés, sections, et chaque produit et groupe
/// d'options une seule fois, avec leurs ids (ceux qu'une commande renverra). Un produit masqué sur ce canal, en
/// brouillon ou archivé n'apparaît pas ; un produit indisponible est montré comme tel.
/// </summary>
public sealed record OrderableCatalogDto(
    string? CurrencyCode,
    IReadOnlyList<OrderableMenuDto> Menus,
    IReadOnlyList<OrderableProductDto> Products,
    IReadOnlyList<OrderableModifierGroupDto> ModifierGroups);

/// <summary>Pour le tenant du contexte (jamais une valeur de requête). Un établissement d'un autre tenant n'a aucun menu.</summary>
public sealed record GetOrderableCatalogQuery(Guid LocationId, OnlineChannel Channel) : IQuery<OrderableCatalogDto>;

/// <summary>
/// Le catalogue commandable d'un établissement sur un canal, à l'instant présent : seuls les menus publiés proposés à
/// cette heure locale (créneaux), et les prix de l'établissement et du canal.
/// </summary>
public sealed class GetOrderableCatalogHandler(CatalogWorkspace workspace, ICatalogLocations? locations = null, IClock? clock = null)
    : IQueryHandler<GetOrderableCatalogQuery, OrderableCatalogDto>
{
    private static readonly OrderableCatalogDto Empty = new(null, [], [], []);

    public async Task<OrderableCatalogDto> HandleAsync(GetOrderableCatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LocationId == Guid.Empty || !Enum.IsDefined(query.Channel))
        {
            return Empty;
        }

        var tenant = workspace.CurrentTenant;
        var local = await LocalNowAsync(tenant, LocationId.From(query.LocationId), cancellationToken);
        var menus = (await workspace.Menus.ListAsync(tenant, LocationId.From(query.LocationId), cancellationToken))
            .Where(menu => menu.Status == MenuStatus.Published && (local is null || menu.IsOfferedAt(local.Value)))
            .ToList();
        var productIds = menus.SelectMany(menu => menu.Sections).SelectMany(section => section.ProductIds).Distinct().ToList();
        var products = (await workspace.Products.FindManyAsync(tenant, productIds, cancellationToken))
            .Where(product => (product.Status is ProductStatus.Active or ProductStatus.Unavailable) && product.IsVisibleOn(query.Channel)
                && product.IsSoldAt(LocationId.From(query.LocationId)))
            .ToDictionary(product => product.Id);
        // Les produits proposés dans les formules, même absents des menus : leur nom, leur disponibilité et leurs options.
        var componentIds = products.Values.SelectMany(product => product.FormulaSteps).SelectMany(step => step.Choices)
            .Select(choice => choice.ProductId).Distinct().ToList();
        var components = componentIds.Count == 0
            ? new Dictionary<ProductId, Product>()
            : (await workspace.Products.FindManyAsync(tenant, componentIds, cancellationToken)).ToDictionary(product => product.Id);
        var groupIds = products.Values.Concat(components.Values).SelectMany(product => product.ModifierGroupIds).Distinct().ToList();
        var groups = (await workspace.ModifierGroups.FindManyAsync(tenant, groupIds, cancellationToken)).ToDictionary(group => group.Id);

        var orderedMenus = menus
            .Select(menu => new OrderableMenuDto(
                menu.Id.Value,
                menu.Name.Value,
                menu.Sections
                    .Select(section => new OrderableSectionDto(
                        section.Id.Value,
                        section.Name.Value,
                        section.ProductIds.Where(products.ContainsKey).Select(id => id.Value).ToList()))
                    .Where(section => section.ProductIds.Count > 0)
                    .ToList()))
            .Where(menu => menu.Sections.Count > 0)
            .OrderBy(menu => menu.Name, StringComparer.CurrentCulture)
            .ToList();

        // Seuls les produits encore présents dans une section, dans l'ordre de leur première apparition.
        var shown = orderedMenus.SelectMany(menu => menu.Sections).SelectMany(section => section.ProductIds).Distinct()
            .Select(id => products[ProductId.From(id)])
            .ToList();
        var shownGroups = shown
            .SelectMany(product => product.ModifierGroupIds.Concat(product.FormulaSteps
                .SelectMany(step => step.Choices)
                .SelectMany(choice => components.TryGetValue(choice.ProductId, out var component) ? component.ModifierGroupIds : [])))
            .Distinct()
            .Where(groups.ContainsKey)
            .Select(id => groups[id])
            .ToList();

        return new OrderableCatalogDto(
            shown.FirstOrDefault()?.Currency.Code,
            orderedMenus,
            shown.Select(product => Product(product, groups, components, LocationId.From(query.LocationId), query.Channel)).ToList(),
            shownGroups.Select(Group).ToList());
    }

    /// <summary>L'heure locale de l'établissement ; null sans horloge (tous les menus publiés).</summary>
    private async Task<DateTime?> LocalNowAsync(TenantId tenant, LocationId locationId, CancellationToken cancellationToken)
    {
        if (clock is null)
        {
            return null;
        }

        var timeZone = locations is not null
            && await locations.FindAsync(tenant, locationId, cancellationToken) is { TimeZoneId: { } id }
            && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found)
                ? found
                : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(clock.UtcNow, timeZone).DateTime;
    }

    private static OrderableProductDto Product(
        Product product,
        IReadOnlyDictionary<ModifierGroupId, ModifierGroup> groups,
        IReadOnlyDictionary<ProductId, Product> components,
        LocationId location,
        OnlineChannel onlineChannel)
    {
        var channel = PriceChannels.Of(onlineChannel);
        var basePrice = product.BasePriceFor(location, channel);
        var attached = product.ModifierGroupIds.Where(groups.ContainsKey).ToList();
        var steps = product.FormulaSteps.Select(step => Step(step, groups, components, location, onlineChannel)).ToList();
        var available = product.IsSellableAt(location)
            && (product.Variants.Count == 0 || product.Variants.Any(variant => variant.IsAvailable))
            && attached.All(id => groups[id].IsSatisfiable)
            && steps.All(step => step.Choices.Count(choice => choice.IsAvailable) >= step.MinChoices);
        return new OrderableProductDto(
            product.Id.Value,
            product.Name.Value,
            product.Description,
            basePrice.Amount,
            available,
            product.Variants
                .Select(variant => new OrderableVariantDto(
                    variant.Id.Value, variant.Name.Value, (basePrice + variant.PriceDelta).Amount, available && variant.IsAvailable))
                .ToList(),
            attached.Select(id => id.Value).ToList(),
            product.Allergens.ToList(),
            steps);
    }

    private static OrderableFormulaStepDto Step(
        FormulaStep step,
        IReadOnlyDictionary<ModifierGroupId, ModifierGroup> groups,
        IReadOnlyDictionary<ProductId, Product> components,
        LocationId location,
        OnlineChannel channel) =>
        new(
            step.Id,
            step.Name.Value,
            step.MinChoices,
            step.MaxChoices,
            step.Choices
                .Where(choice => components.ContainsKey(choice.ProductId) && components[choice.ProductId].Status != ProductStatus.Archived)
                .Select(choice =>
                {
                    var component = components[choice.ProductId];
                    var variant = choice.VariantId is { } id ? component.Variants.FirstOrDefault(item => item.Id == id) : null;
                    var attached = component.ModifierGroupIds.Where(groups.ContainsKey).ToList();
                    var available = component.IsSellableAt(location)
                        && component.IsVisibleOn(channel)
                        && (choice.VariantId is null || variant is { IsAvailable: true })
                        && attached.All(groupId => groups[groupId].IsSatisfiable);
                    return new OrderableFormulaChoiceDto(
                        component.Id.Value,
                        variant?.Id.Value,
                        component.Name.Value,
                        variant?.Name.Value,
                        choice.Supplement.Amount,
                        available,
                        attached.Select(groupId => groupId.Value).ToList(),
                        component.Allergens.ToList());
                })
                .ToList());

    private static OrderableModifierGroupDto Group(ModifierGroup group) =>
        new(
            group.Id.Value,
            group.Name.Value,
            group.MinSelections,
            group.MaxSelections,
            group.Modifiers.Select(modifier => new OrderableModifierDto(modifier.Id.Value, modifier.Name.Value, modifier.PriceDelta.Amount, modifier.IsAvailable)).ToList());
}

/// <summary>Le canal de prix d'un canal de vente en ligne.</summary>
public static class PriceChannels
{
    public static PriceChannel Of(OnlineChannel channel) => channel switch
    {
        OnlineChannel.MobileApp => PriceChannel.MobileApp,
        OnlineChannel.Kiosk => PriceChannel.Kiosk,
        _ => PriceChannel.Web,
    };
}

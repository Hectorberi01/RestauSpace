using Catalog.Domain;

namespace Catalog.Application.Queries;

/// <summary>Une option choisie, telle qu'une ligne de commande la fige.</summary>
public sealed record SaleModifierDto(Guid ModifierId, string Name, decimal PriceDelta);

/// <summary>Un produit choisi dans une formule, avec ses options, tel qu'une ligne de commande le fige.</summary>
/// <param name="Supplement">Supplément de ce choix dans la formule ; le prix propre du produit n'entre pas en compte.</param>
public sealed record SaleComponentDto(
    Guid StepId,
    string StepName,
    Guid ProductId,
    string ProductName,
    Guid? VariantId,
    string? VariantName,
    decimal Supplement,
    IReadOnlyList<SaleModifierDto> Modifiers);

/// <summary>
/// Tout ce qu'une ligne de commande fige d'un produit au moment de la vente : nom, variante, prix unitaire (base +
/// delta de variante, tel que saisi au catalogue : TTC ou HT selon le réglage du tenant), taxe et options choisies.
/// Pour une formule : les produits choisis à chaque étape (<paramref name="Components"/>), avec leurs suppléments et
/// leurs options ; la taxe est celle de la formule.
/// </summary>
public sealed record SellableItemDto(
    Guid ProductId,
    string ProductName,
    Guid? VariantId,
    string? VariantName,
    string CurrencyCode,
    decimal UnitPrice,
    string TaxCategoryCode,
    string TaxCategoryName,
    decimal TaxRate,
    IReadOnlyList<SaleModifierDto> Modifiers,
    IReadOnlyList<SaleComponentDto>? Components = null);

/// <summary>Un produit choisi à une étape d'une formule, avec sa variante et ses options.</summary>
public sealed record SaleComponentInput(Guid StepId, Guid ProductId, Guid? VariantId = null, IReadOnlyList<Guid>? ModifierIds = null);

/// <summary>
/// Pour le module Orders, via l'hôte : décrit la vente d'un produit du tenant courant, ou la refuse. Le produit doit
/// être actif (et la variante disponible, obligatoire pour un produit à variantes) ; chaque groupe d'options attaché doit
/// recevoir une sélection valide (min/max, options disponibles et distinctes) ; une option qui n'appartient à aucun
/// groupe du produit est refusée. Une formule exige ses composants : chacun proposé à son étape, actif et disponible,
/// avec ses propres options valides, et le nombre choisi à chaque étape dans ses bornes.
/// </summary>
/// <param name="LocationId">Établissement de la vente : son prix propre s'il en a un.</param>
/// <param name="Channel">Canal de la vente : son prix propre s'il en a un.</param>
public sealed record GetSellableItemQuery(
    Guid ProductId,
    Guid? VariantId = null,
    IReadOnlyList<Guid>? ModifierIds = null,
    Guid? LocationId = null,
    PriceChannel? Channel = null,
    IReadOnlyList<SaleComponentInput>? Components = null)
    : IQuery<SellableItemDto>;

public sealed class GetSellableItemHandler(CatalogWorkspace workspace) : IQueryHandler<GetSellableItemQuery, SellableItemDto>
{
    public const int MaxComponents = 20;

    public async Task<SellableItemDto> HandleAsync(GetSellableItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, query.ProductId, cancellationToken);
        var location = query.LocationId is { } id && id != Guid.Empty ? LocationId.From(id) : (LocationId?)null;
        var item = product.DescribeForSale(query.VariantId is { } variant ? VariantId.From(variant) : null, location, query.Channel);
        var tax = (await workspace.LoadTaxCategoryAsync(tenant, item.TaxCategoryId.Value, cancellationToken)).ToReference();
        var modifiers = await ChosenModifiersAsync(tenant, product, query.ModifierIds, cancellationToken);
        var components = await ComponentsAsync(tenant, product, query.Components ?? [], location, query.Channel, cancellationToken);

        return new SellableItemDto(
            item.ProductId.Value,
            item.ProductName,
            item.VariantId?.Value,
            item.VariantName,
            item.UnitPrice.Currency.Code,
            item.UnitPrice.Amount,
            tax.Code,
            tax.Name,
            tax.Rate,
            modifiers,
            components);
    }

    private async Task<IReadOnlyList<SaleComponentDto>> ComponentsAsync(
        TenantId tenant, Product formula, IReadOnlyList<SaleComponentInput> inputs, LocationId? location, PriceChannel? channel, CancellationToken cancellationToken)
    {
        DomainException.ThrowIf(inputs.Count > MaxComponents, FormulaErrors.SelectionCountInvalid);
        var chosen = formula.ChooseInFormula(inputs
            .Select(input => (
                input.StepId,
                input.ProductId == Guid.Empty ? throw new DomainException(FormulaErrors.ChoiceNotOffered) : ProductId.From(input.ProductId),
                input.VariantId is { } variant ? VariantId.From(variant) : (VariantId?)null))
            .ToList());
        if (chosen.Count == 0)
        {
            return [];
        }

        var products = (await workspace.Products.FindManyAsync(tenant, chosen.Select(item => item.Choice.ProductId).Distinct().ToList(), cancellationToken))
            .ToDictionary(product => product.Id);
        var result = new List<SaleComponentDto>(chosen.Count);
        for (var index = 0; index < chosen.Count; index++)
        {
            var (step, choice) = chosen[index];
            var component = products.GetValueOrDefault(choice.ProductId) ?? throw new DomainException(ProductErrors.NotSellable);
            var described = component.DescribeForSale(choice.VariantId, location, channel);
            var modifiers = await ChosenModifiersAsync(tenant, component, inputs[index].ModifierIds, cancellationToken);
            result.Add(new SaleComponentDto(
                step.Id, step.Name.Value, described.ProductId.Value, described.ProductName, described.VariantId?.Value, described.VariantName,
                choice.Supplement.Amount, modifiers));
        }

        return result;
    }

    private async Task<IReadOnlyList<SaleModifierDto>> ChosenModifiersAsync(
        TenantId tenant, Product product, IReadOnlyList<Guid>? modifierIds, CancellationToken cancellationToken)
    {
        var chosen = (modifierIds ?? Array.Empty<Guid>()).Select(ModifierId.From).ToList();
        var groups = await workspace.ModifierGroups.FindManyAsync(tenant, product.ModifierGroupIds, cancellationToken);
        var offered = new Dictionary<ModifierId, Modifier>();
        foreach (var group in groups)
        {
            var inGroup = group.Modifiers.Select(modifier => modifier.Id).ToHashSet();
            // Chaque groupe valide sa propre part du choix (doublons compris), même vide : un groupe obligatoire l'exige.
            group.EnsureSelectionIsValid(chosen.Where(inGroup.Contains));
            foreach (var modifier in group.Modifiers)
            {
                offered.TryAdd(modifier.Id, modifier);
            }
        }

        DomainException.ThrowIf(chosen.Any(id => !offered.ContainsKey(id)), CatalogApplicationErrors.ModifierNotOffered);
        return chosen.Select(id => offered[id]).Select(modifier => new SaleModifierDto(modifier.Id.Value, modifier.Name.Value, modifier.PriceDelta.Amount)).ToList();
    }
}

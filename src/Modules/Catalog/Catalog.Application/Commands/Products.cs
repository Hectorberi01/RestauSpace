using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.Commands;

// Produits. Les montants sont dans la devise du produit (celle du tenant à sa création) : la requête ne choisit jamais
// la devise. Draft -> Active <-> Unavailable ; tout état -> Archived (terminal).

/// <summary>
/// Crée un produit en Draft, dans la devise par défaut du tenant, avec une catégorie fiscale active du même tenant.
/// Renvoie son id.
/// </summary>
public sealed record CreateProductCommand(string? Name, string? Description, decimal BasePrice, Guid TaxCategoryId)
    : ICommand<Guid>;

public sealed class CreateProductHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await workspace.RequireWritableTenantAsync(cancellationToken);
        var name = CatalogName.Create(command.Name);
        var basePrice = Money.Of(command.BasePrice, tenant.DefaultCurrency);
        var taxCategory = await workspace.LoadTaxCategoryAsync(workspace.CurrentTenant, command.TaxCategoryId, cancellationToken);

        var product = Product.Create(
            workspace.CurrentTenant, name, command.Description, basePrice, taxCategory.ToReference(), clock);

        workspace.Products.Add(product);
        await workspace.SaveAsync(cancellationToken);
        return product.Id.Value;
    }
}

public sealed record RenameProductCommand(Guid ProductId, string? Name) : ICommand;

public sealed class RenameProductHandler(CatalogWorkspace workspace) : ICommandHandler<RenameProductCommand>
{
    public Task HandleAsync(RenameProductCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.Rename(CatalogName.Create(command.Name)), cancellationToken);
}

/// <summary>Null ou blanc efface la description.</summary>
public sealed record ChangeProductDescriptionCommand(Guid ProductId, string? Description) : ICommand;

public sealed class ChangeProductDescriptionHandler(CatalogWorkspace workspace) : ICommandHandler<ChangeProductDescriptionCommand>
{
    public Task HandleAsync(ChangeProductDescriptionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.ChangeDescription(command.Description), cancellationToken);
}

/// <summary>Ventes futures uniquement. La nouvelle catégorie doit être active et du même tenant.</summary>
public sealed record ChangeProductTaxCategoryCommand(Guid ProductId, Guid TaxCategoryId) : ICommand;

public sealed class ChangeProductTaxCategoryHandler(CatalogWorkspace workspace) : ICommandHandler<ChangeProductTaxCategoryCommand>
{
    public async Task HandleAsync(ChangeProductTaxCategoryCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        var taxCategory = await workspace.LoadTaxCategoryAsync(tenant, command.TaxCategoryId, cancellationToken);

        product.ChangeTaxCategory(taxCategory.ToReference());

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ChangeProductBasePriceCommand(Guid ProductId, decimal BasePrice) : ICommand;

public sealed class ChangeProductBasePriceHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ChangeProductBasePriceCommand>
{
    public Task HandleAsync(ChangeProductBasePriceCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId,
            product => product.ChangeBasePrice(Money.Of(command.BasePrice, product.Currency), clock),
            cancellationToken);
}

public sealed record ActivateProductCommand(Guid ProductId) : ICommand;

public sealed class ActivateProductHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ActivateProductCommand>
{
    public Task HandleAsync(ActivateProductCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.Activate(clock), cancellationToken);
}

public sealed record MarkProductUnavailableCommand(Guid ProductId) : ICommand;

public sealed class MarkProductUnavailableHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<MarkProductUnavailableCommand>
{
    public Task HandleAsync(MarkProductUnavailableCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.MarkUnavailable(clock), cancellationToken);
}

public sealed record ArchiveProductCommand(Guid ProductId) : ICommand;

public sealed class ArchiveProductHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ArchiveProductCommand>
{
    public Task HandleAsync(ArchiveProductCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.Archive(clock), cancellationToken);
}

/// <summary>
/// Ajoute une variante disponible. <paramref name="PriceDelta"/> peut être négatif tant que prix de base + delta >= 0.
/// Renvoie son id.
/// </summary>
public sealed record AddProductVariantCommand(Guid ProductId, string? Name, decimal PriceDelta, string? Sku = null) : ICommand<Guid>;

public sealed class AddProductVariantHandler(CatalogWorkspace workspace) : ICommandHandler<AddProductVariantCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddProductVariantCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var product = await workspace.LoadProductAsync(workspace.CurrentTenant, command.ProductId, cancellationToken);

        var id = product.AddVariant(
            CatalogName.Create(command.Name),
            Money.Of(command.PriceDelta, product.Currency),
            string.IsNullOrWhiteSpace(command.Sku) ? null : Sku.Create(command.Sku));

        await workspace.SaveAsync(cancellationToken);
        return id.Value;
    }
}

public sealed record RemoveProductVariantCommand(Guid ProductId, Guid VariantId) : ICommand;

public sealed class RemoveProductVariantHandler(CatalogWorkspace workspace) : ICommandHandler<RemoveProductVariantCommand>
{
    public Task HandleAsync(RemoveProductVariantCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId, product => product.RemoveVariant(VariantId.From(command.VariantId)), cancellationToken);
}

public sealed record ChangeVariantPriceDeltaCommand(Guid ProductId, Guid VariantId, decimal PriceDelta) : ICommand;

public sealed class ChangeVariantPriceDeltaHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ChangeVariantPriceDeltaCommand>
{
    public Task HandleAsync(ChangeVariantPriceDeltaCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId,
            product => product.ChangeVariantPriceDelta(
                VariantId.From(command.VariantId), Money.Of(command.PriceDelta, product.Currency), clock),
            cancellationToken);
}

public sealed record SetVariantAvailabilityCommand(Guid ProductId, Guid VariantId, bool IsAvailable) : ICommand;

public sealed class SetVariantAvailabilityHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<SetVariantAvailabilityCommand>
{
    public Task HandleAsync(SetVariantAvailabilityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId,
            product => product.SetVariantAvailability(VariantId.From(command.VariantId), command.IsAvailable, clock),
            cancellationToken);
}

/// <summary>Le groupe doit être du même tenant et dans la devise du produit.</summary>
public sealed record AttachModifierGroupCommand(Guid ProductId, Guid ModifierGroupId) : ICommand;

public sealed class AttachModifierGroupHandler(CatalogWorkspace workspace) : ICommandHandler<AttachModifierGroupCommand>
{
    public async Task HandleAsync(AttachModifierGroupCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        var group = await workspace.LoadModifierGroupAsync(tenant, command.ModifierGroupId, cancellationToken);

        product.AttachModifierGroup(group.ToReference());

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record DetachModifierGroupCommand(Guid ProductId, Guid ModifierGroupId) : ICommand;

public sealed class DetachModifierGroupHandler(CatalogWorkspace workspace) : ICommandHandler<DetachModifierGroupCommand>
{
    public Task HandleAsync(DetachModifierGroupCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId,
            product => product.DetachModifierGroup(ModifierGroupId.From(command.ModifierGroupId)),
            cancellationToken);
}

/// <summary>Remplace les allergènes du produit par des codes du référentiel (14 allergènes UE) ; liste vide = aucun.</summary>
public sealed record SetProductAllergensCommand(Guid ProductId, IReadOnlyList<string?>? Allergens) : ICommand;

public sealed class SetProductAllergensHandler(CatalogWorkspace workspace) : ICommandHandler<SetProductAllergensCommand>
{
    public Task HandleAsync(SetProductAllergensCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.SetAllergens(command.Allergens ?? []), cancellationToken);
}

/// <summary>Remplace les canaux de vente en ligne où le produit est masqué ; liste vide = visible partout.</summary>
public sealed record SetProductHiddenChannelsCommand(Guid ProductId, IReadOnlyList<OnlineChannel>? Channels) : ICommand;

public sealed class SetProductHiddenChannelsHandler(CatalogWorkspace workspace) : ICommandHandler<SetProductHiddenChannelsCommand>
{
    public Task HandleAsync(SetProductHiddenChannelsCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(command.ProductId, product => product.SetHiddenChannels(command.Channels ?? []), cancellationToken);
}

/// <summary>
/// Pose le prix de base du produit pour un établissement de ce restaurant, un canal, ou les deux ; les variantes gardent
/// leur écart avec ce prix.
/// </summary>
public sealed record SetProductPriceOverrideCommand(Guid ProductId, Guid? LocationId, PriceChannel? Channel, decimal BasePrice) : ICommand;

public sealed class SetProductPriceOverrideHandler(CatalogWorkspace workspace, ICatalogLocations locations, IClock clock)
    : ICommandHandler<SetProductPriceOverrideCommand>
{
    public async Task HandleAsync(SetProductPriceOverrideCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        LocationReference? location = null;
        if (command.LocationId is { } locationId)
        {
            location = (locationId == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(locationId), cancellationToken))
                ?? throw new NotFoundException(CatalogApplicationErrors.LocationNotFound);
        }

        product.SetPriceOverride(location, command.Channel, Money.Of(command.BasePrice, product.Currency), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Les établissements de ce restaurant où le produit est vendu ; liste vide : tous.</summary>
public sealed record SetProductLocationsCommand(Guid ProductId, IReadOnlyList<Guid>? LocationIds) : ICommand;

public sealed class SetProductLocationsHandler(CatalogWorkspace workspace, ICatalogLocations locations, IClock clock)
    : ICommandHandler<SetProductLocationsCommand>
{
    public async Task HandleAsync(SetProductLocationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        var references = new List<LocationReference>();
        foreach (var id in (command.LocationIds ?? []).Distinct())
        {
            references.Add((id == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(id), cancellationToken))
                ?? throw new NotFoundException(CatalogApplicationErrors.LocationNotFound));
        }

        product.SetLocations(references, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Rupture (ou retour en vente) du produit dans un seul établissement.</summary>
public sealed record SetProductAvailabilityAtLocationCommand(Guid ProductId, Guid LocationId, bool IsAvailable) : ICommand;

public sealed class SetProductAvailabilityAtLocationHandler(CatalogWorkspace workspace, ICatalogLocations locations, IClock clock)
    : ICommandHandler<SetProductAvailabilityAtLocationCommand>
{
    public async Task HandleAsync(SetProductAvailabilityAtLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        var location = (command.LocationId == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(CatalogApplicationErrors.LocationNotFound);
        product.SetAvailabilityAt(location, command.IsAvailable, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Retire un prix propre : le produit reprend le prix moins précis, ou son prix de base.</summary>
public sealed record RemoveProductPriceOverrideCommand(Guid ProductId, Guid? LocationId, PriceChannel? Channel) : ICommand;

public sealed class RemoveProductPriceOverrideHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<RemoveProductPriceOverrideCommand>
{
    public Task HandleAsync(RemoveProductPriceOverrideCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditProductAsync(
            command.ProductId,
            product => product.RemovePriceOverride(
                command.LocationId is { } location && location != Guid.Empty ? LocationId.From(location) : null, command.Channel, clock),
            cancellationToken);
}

/// <summary>Un produit proposé à une étape de formule : sa variante s'il en a, et le supplément ajouté au prix (0 par défaut).</summary>
public sealed record FormulaChoiceInput(Guid ProductId, Guid? VariantId = null, decimal Supplement = 0m);

/// <summary>Une étape de formule (« Plat », « Boisson ») : de <paramref name="MinChoices"/> à <paramref name="MaxChoices"/> produits.</summary>
/// <param name="Id">L'id d'une étape existante pour la garder ; null pour une nouvelle étape.</param>
public sealed record FormulaStepInput(Guid? Id, string? Name, int MinChoices, int MaxChoices, IReadOnlyList<FormulaChoiceInput>? Choices);

/// <summary>
/// Fait du produit une formule (menu composé) ou change ses étapes ; aucune étape en refait un produit ordinaire. Les
/// produits proposés sont du même restaurant, ni archivés ni eux-mêmes des formules ; un produit à variantes est
/// proposé par l'une d'elles.
/// </summary>
public sealed record SetProductFormulaCommand(Guid ProductId, IReadOnlyList<FormulaStepInput>? Steps) : ICommand;

public sealed class SetProductFormulaHandler(CatalogWorkspace workspace) : ICommandHandler<SetProductFormulaCommand>
{
    public async Task HandleAsync(SetProductFormulaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var formula = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        var inputs = command.Steps ?? [];
        var wanted = inputs.SelectMany(step => step.Choices ?? []).Select(choice => choice.ProductId).Distinct().ToList();
        var products = wanted.Count == 0
            ? new Dictionary<ProductId, Product>()
            : (await workspace.Products.FindManyAsync(tenant, wanted.Where(id => id != Guid.Empty).Select(ProductId.From).ToList(), cancellationToken))
                .ToDictionary(product => product.Id);

        var steps = inputs
            .Select(step => FormulaStep.Create(
                step.Id,
                CatalogName.Create(step.Name),
                step.MinChoices,
                step.MaxChoices,
                (step.Choices ?? [])
                    .Select(choice =>
                    {
                        var product = choice.ProductId != Guid.Empty && products.TryGetValue(ProductId.From(choice.ProductId), out var found)
                            ? found
                            : throw new NotFoundException(CatalogApplicationErrors.ProductNotFound);
                        return product.OfferInFormula(
                            choice.VariantId is { } variant ? VariantId.From(variant) : null, Money.Of(choice.Supplement, formula.Currency));
                    })
                    .ToList()))
            .ToList();
        formula.SetFormulaSteps(steps);
        await workspace.SaveAsync(cancellationToken);
    }
}

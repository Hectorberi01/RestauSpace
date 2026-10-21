using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application.Commands;

// Le stock d'un ingrédient dans un établissement (un article par couple) et ses mouvements. Le registre des mouvements
// fait foi ; les règles (stock négatif, unités, seuil bas) sont celles de l'agrégat InventoryItem. Une quantité sans
// unité est exprimée dans l'unité de l'article.

/// <summary>
/// Crée l'article de stock d'un ingrédient actif dans un établissement du restaurant, stock à zéro. Unité : celle de
/// l'ingrédient si omise (de la même dimension sinon). Seuil bas : zéro si omis. Renvoie son id.
/// </summary>
public sealed record CreateInventoryItemCommand(
    Guid LocationId,
    Guid IngredientId,
    UnitOfMeasure? Unit = null,
    QuantityInput? ReorderThreshold = null,
    NegativeStockPolicy NegativeStockPolicy = NegativeStockPolicy.Forbid) : ICommand<Guid>;

public sealed class CreateInventoryItemHandler(InventoryWorkspace workspace, IInventoryItemUniquenessChecker uniqueness)
    : ICommandHandler<CreateInventoryItemCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateInventoryItemCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var ingredient = await workspace.LoadIngredientAsync(tenant, command.IngredientId, cancellationToken);
        var unit = command.Unit ?? ingredient.BaseUnit;
        var threshold = command.ReorderThreshold is null ? Quantity.Zero(unit) : QuantityInput.Require(command.ReorderThreshold, unit);

        var item = InventoryItem.Create(
            tenant,
            location,
            ingredient.ToReference(),
            unit,
            threshold,
            await uniqueness.IsUniqueAsync(tenant, location.LocationId, ingredient.Id, cancellationToken),
            command.NegativeStockPolicy);

        workspace.Items.Add(item);
        await workspace.SaveAsync(cancellationToken);
        return item.Id.Value;
    }
}

/// <summary>
/// Entrée de stock (livraison fournisseur...) ; <paramref name="Reference"/> : bon de livraison, facture.
/// <paramref name="TotalCost"/> : montant payé pour cette livraison (devise du restaurant), qui met à jour le coût moyen.
/// </summary>
public sealed record ReceiveStockCommand(Guid ItemId, QuantityInput? Quantity, string? Reference = null, decimal? TotalCost = null) : ICommand;

public sealed class ReceiveStockHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<ReceiveStockCommand>
{
    public Task HandleAsync(ReceiveStockCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(
            command.ItemId,
            item => item.Receive(QuantityInput.Require(command.Quantity, item.Unit), clock, command.Reference, command.TotalCost),
            cancellationToken);
}

/// <summary>Coût d'une unité de l'article saisi à la main (devise du restaurant) ; null l'efface.</summary>
public sealed record SetUnitCostCommand(Guid ItemId, decimal? UnitCost) : ICommand;

public sealed class SetUnitCostHandler(InventoryWorkspace workspace) : ICommandHandler<SetUnitCostCommand>
{
    public Task HandleAsync(SetUnitCostCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(command.ItemId, item => item.SetUnitCost(command.UnitCost), cancellationToken);
}

/// <summary>Sortie manuelle (hors vente) ; refusée sous zéro si l'article interdit le stock négatif.</summary>
public sealed record ConsumeStockCommand(Guid ItemId, QuantityInput? Quantity, string? Reference = null) : ICommand;

public sealed class ConsumeStockHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<ConsumeStockCommand>
{
    public Task HandleAsync(ConsumeStockCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(
            command.ItemId, item => item.Consume(QuantityInput.Require(command.Quantity, item.Unit), clock, command.Reference), cancellationToken);
}

/// <summary>Perte (casse, péremption...) ; motif obligatoire.</summary>
public sealed record RecordWasteCommand(Guid ItemId, QuantityInput? Quantity, string? Reason, string? Reference = null) : ICommand;

public sealed class RecordWasteHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<RecordWasteCommand>
{
    public Task HandleAsync(RecordWasteCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(
            command.ItemId,
            item => item.RecordWaste(QuantityInput.Require(command.Quantity, item.Unit), command.Reason ?? string.Empty, clock, command.Reference),
            cancellationToken);
}

/// <summary>
/// Inventaire physique : le stock devient la quantité comptée. Le registre garde la différence (ou la confirmation si
/// rien n'a bougé) ; motif obligatoire.
/// </summary>
public sealed record CountStockCommand(Guid ItemId, QuantityInput? CountedQuantity, string? Reason, string? Reference = null) : ICommand;

public sealed class CountStockHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<CountStockCommand>
{
    public Task HandleAsync(CountStockCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(
            command.ItemId,
            item => item.Adjust(QuantityInput.Require(command.CountedQuantity, item.Unit), command.Reason ?? string.Empty, clock, command.Reference),
            cancellationToken);
}

/// <summary>
/// Transfert entre deux établissements du restaurant : la même quantité quitte la source et entre dans la destination,
/// sous la même référence, dans une seule transaction. Unité par défaut : celle de la source. Sans référence (bon de
/// transfert), une référence unique est générée.
/// </summary>
public sealed record TransferStockCommand(Guid SourceItemId, Guid DestinationItemId, QuantityInput? Quantity, string? Reference) : ICommand;

public sealed class TransferStockHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<TransferStockCommand>
{
    public async Task HandleAsync(TransferStockCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var source = await workspace.LoadItemAsync(tenant, command.SourceItemId, cancellationToken);
        var destination = command.DestinationItemId == command.SourceItemId
            ? source
            : await workspace.LoadItemAsync(tenant, command.DestinationItemId, cancellationToken);

        var reference = string.IsNullOrWhiteSpace(command.Reference) ? $"TR-{Guid.CreateVersion7():N}" : command.Reference;
        StockTransfer.Execute(source, destination, QuantityInput.Require(command.Quantity, source.Unit), reference, clock);

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Seuil bas (en dessous ou égal : alerte) ; unité de l'article si omise.</summary>
public sealed record ChangeReorderThresholdCommand(Guid ItemId, QuantityInput? Threshold) : ICommand;

public sealed class ChangeReorderThresholdHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<ChangeReorderThresholdCommand>
{
    public Task HandleAsync(ChangeReorderThresholdCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(
            command.ItemId, item => item.ChangeReorderThreshold(QuantityInput.Require(command.Threshold, item.Unit), clock), cancellationToken);
}

public sealed record ChangeNegativeStockPolicyCommand(Guid ItemId, NegativeStockPolicy Policy) : ICommand;

public sealed class ChangeNegativeStockPolicyHandler(InventoryWorkspace workspace) : ICommandHandler<ChangeNegativeStockPolicyCommand>
{
    public Task HandleAsync(ChangeNegativeStockPolicyCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditItemAsync(command.ItemId, item => item.ChangeNegativeStockPolicy(command.Policy), cancellationToken);
}

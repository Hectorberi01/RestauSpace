using Inventory.Application.Abstractions;
using Inventory.Application.Queries;
using Inventory.Domain;

namespace Inventory.Application.Commands;

// Achats : fournisseurs, bons de commande, réception rapprochée du bon (entrées de stock chiffrées), suggestions de
// réassort d'après les seuils, et coût matière des recettes.

public sealed record SupplierDto(Guid Id, string Name, string? Phone, string? Email, string? Notes, bool IsActive)
{
    public static SupplierDto From(Supplier supplier) =>
        new(supplier.Id.Value, supplier.Name, supplier.Phone, supplier.Email, supplier.Notes, supplier.IsActive);
}

public sealed record SaveSupplierCommand(Guid? SupplierId, string? Name, string? Phone, string? Email, string? Notes) : ICommand<Guid>;

public sealed class SaveSupplierHandler(InventoryWorkspace workspace, ISupplierRepository suppliers, IClock clock) : ICommandHandler<SaveSupplierCommand, Guid>
{
    public async Task<Guid> HandleAsync(SaveSupplierCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        if (command.SupplierId is { } id)
        {
            var existing = await suppliers.LoadAsync(tenant, id, cancellationToken);
            existing.Change(command.Name, command.Phone, command.Email, command.Notes);
            await workspace.SaveAsync(cancellationToken);
            return existing.Id.Value;
        }

        var supplier = Supplier.Create(tenant, command.Name, command.Phone, command.Email, command.Notes, clock);
        suppliers.Add(supplier);
        await workspace.SaveAsync(cancellationToken);
        return supplier.Id.Value;
    }
}

public sealed record SetSupplierActiveCommand(Guid SupplierId, bool IsActive) : ICommand;

public sealed class SetSupplierActiveHandler(InventoryWorkspace workspace, ISupplierRepository suppliers) : ICommandHandler<SetSupplierActiveCommand>
{
    public async Task HandleAsync(SetSupplierActiveCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var supplier = await suppliers.LoadAsync(workspace.CurrentTenant, command.SupplierId, cancellationToken);
        if (command.IsActive)
        {
            supplier.Reactivate();
        }
        else
        {
            supplier.Deactivate();
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ListSuppliersQuery : IQuery<IReadOnlyList<SupplierDto>>;

public sealed class ListSuppliersHandler(InventoryWorkspace workspace, ISupplierRepository suppliers) : IQueryHandler<ListSuppliersQuery, IReadOnlyList<SupplierDto>>
{
    public async Task<IReadOnlyList<SupplierDto>> HandleAsync(ListSuppliersQuery query, CancellationToken cancellationToken = default) =>
        (await suppliers.ListAsync(workspace.CurrentTenant, cancellationToken)).Select(SupplierDto.From).ToList();
}

// --- Bons de commande ---

public sealed record PurchaseLineInput(Guid ItemId, QuantityInput? Quantity, decimal? UnitPrice = null);

public sealed record PurchaseOrderLineDto(Guid ItemId, Guid IngredientId, string? IngredientName, UnitOfMeasure Unit, decimal Ordered, decimal? UnitPrice, decimal Received);

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid LocationId,
    Guid SupplierId,
    string? SupplierName,
    PurchaseOrderStatus Status,
    string? Notes,
    decimal? EstimatedTotal,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? ReceivedAtUtc);

/// <summary>Nouveau bon (brouillon) chez un fournisseur actif, pour le stock d'un établissement. Renvoie son id.</summary>
public sealed record CreatePurchaseOrderCommand(Guid LocationId, Guid SupplierId, IReadOnlyList<PurchaseLineInput>? Lines, string? Notes) : ICommand<Guid>;

public sealed class CreatePurchaseOrderHandler(InventoryWorkspace workspace, ISupplierRepository suppliers, IPurchaseOrderRepository orders, IClock clock)
    : ICommandHandler<CreatePurchaseOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var supplier = await suppliers.LoadAsync(tenant, command.SupplierId, cancellationToken);
        var order = PurchaseOrder.Create(tenant, location, supplier, clock);
        order.Compose(await PurchaseLines.ResolveAsync(workspace, tenant, command.Lines, cancellationToken), command.Notes);
        orders.Add(order);
        await workspace.SaveAsync(cancellationToken);
        return order.Id.Value;
    }
}

public sealed record ComposePurchaseOrderCommand(Guid PurchaseOrderId, IReadOnlyList<PurchaseLineInput>? Lines, string? Notes) : ICommand;

public sealed class ComposePurchaseOrderHandler(InventoryWorkspace workspace, IPurchaseOrderRepository orders) : ICommandHandler<ComposePurchaseOrderCommand>
{
    public async Task HandleAsync(ComposePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await orders.LoadAsync(tenant, command.PurchaseOrderId, cancellationToken);
        order.Compose(await PurchaseLines.ResolveAsync(workspace, tenant, command.Lines, cancellationToken), command.Notes);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record SendPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand;

public sealed class SendPurchaseOrderHandler(InventoryWorkspace workspace, IPurchaseOrderRepository orders, IClock clock) : ICommandHandler<SendPurchaseOrderCommand>
{
    public async Task HandleAsync(SendPurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var order = await orders.LoadAsync(workspace.CurrentTenant, command.PurchaseOrderId, cancellationToken);
        order.Send(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand;

public sealed class CancelPurchaseOrderHandler(InventoryWorkspace workspace, IPurchaseOrderRepository orders) : ICommandHandler<CancelPurchaseOrderCommand>
{
    public async Task HandleAsync(CancelPurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var order = await orders.LoadAsync(workspace.CurrentTenant, command.PurchaseOrderId, cancellationToken);
        order.Cancel();
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ReceivedLineInput(Guid ItemId, decimal Quantity, decimal? UnitPrice = null);

/// <summary>
/// Livraison rapprochée du bon : chaque quantité reçue entre au stock (référence = numéro du bon) avec son coût, et
/// le bon passe reçu en partie ou en totalité. <paramref name="Close"/> : le reste ne viendra pas.
/// </summary>
public sealed record ReceivePurchaseOrderCommand(Guid PurchaseOrderId, IReadOnlyList<ReceivedLineInput>? Lines, bool Close = false) : ICommand;

public sealed class ReceivePurchaseOrderHandler(InventoryWorkspace workspace, IPurchaseOrderRepository orders, IClock clock)
    : ICommandHandler<ReceivePurchaseOrderCommand>
{
    public async Task HandleAsync(ReceivePurchaseOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await orders.LoadAsync(tenant, command.PurchaseOrderId, cancellationToken);
        var receipts = order.Receive(
            (command.Lines ?? []).Select(line => (InventoryItemId.From(line.ItemId), line.Quantity, line.UnitPrice)).ToList(), clock);
        foreach (var receipt in receipts)
        {
            var item = await workspace.LoadItemAsync(tenant, receipt.ItemId.Value, cancellationToken);
            item.Receive(receipt.Quantity, clock, order.Number, receipt.TotalCost);
        }

        if (command.Close && order.Status == PurchaseOrderStatus.PartiallyReceived)
        {
            order.CloseAsReceived(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ListPurchaseOrdersQuery(Guid? LocationId = null, PurchaseOrderStatus? Status = null) : IQuery<IReadOnlyList<PurchaseOrderDto>>;

public sealed class ListPurchaseOrdersHandler(InventoryWorkspace workspace, ISupplierRepository suppliers, IPurchaseOrderRepository orders)
    : IQueryHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>>
{
    public async Task<IReadOnlyList<PurchaseOrderDto>> HandleAsync(ListPurchaseOrdersQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var found = await orders.ListAsync(
            tenant, query.LocationId is { } id && id != Guid.Empty ? LocationId.From(id) : null, query.Status, 200, cancellationToken);
        return await PurchaseLines.DescribeAsync(workspace, suppliers, tenant, found, cancellationToken);
    }
}

public sealed record GetPurchaseOrderQuery(Guid PurchaseOrderId) : IQuery<PurchaseOrderDto>;

public sealed class GetPurchaseOrderHandler(InventoryWorkspace workspace, ISupplierRepository suppliers, IPurchaseOrderRepository orders)
    : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(GetPurchaseOrderQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var order = await orders.LoadAsync(tenant, query.PurchaseOrderId, cancellationToken);
        return (await PurchaseLines.DescribeAsync(workspace, suppliers, tenant, [order], cancellationToken)).Single();
    }
}

// --- Réassort et coût matière ---

/// <param name="SuggestedQuantity">De quoi remonter au double du seuil, dans l'unité de l'article.</param>
public sealed record ReorderSuggestionDto(Guid ItemId, Guid IngredientId, string? IngredientName, UnitOfMeasure Unit, decimal CurrentQuantity, decimal ReorderThreshold, decimal SuggestedQuantity, decimal? AverageUnitCost);

/// <summary>Articles au seuil ou en dessous dans un établissement, et la quantité à commander pour revenir au double du seuil.</summary>
public sealed record GetReorderSuggestionsQuery(Guid LocationId) : IQuery<IReadOnlyList<ReorderSuggestionDto>>;

public sealed class GetReorderSuggestionsHandler(InventoryWorkspace workspace) : IQueryHandler<GetReorderSuggestionsQuery, IReadOnlyList<ReorderSuggestionDto>>
{
    public async Task<IReadOnlyList<ReorderSuggestionDto>> HandleAsync(GetReorderSuggestionsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var tenant = workspace.CurrentTenant;
        var low = (await workspace.Items.ListAsync(tenant, LocationId.From(query.LocationId), lowStockOnly: true, cancellationToken))
            .Where(item => item.ReorderThreshold.Amount > 0m)
            .ToList();
        var names = await IngredientNames.OfAsync(workspace, tenant, low.Select(item => item.IngredientId), cancellationToken);
        return low
            .Select(item => new ReorderSuggestionDto(
                item.Id.Value,
                item.IngredientId.Value,
                names.GetValueOrDefault(item.IngredientId),
                item.Unit,
                item.CurrentQuantity,
                item.ReorderThreshold.Amount,
                Math.Ceiling(Math.Max(item.ReorderThreshold.Amount * 2m - item.CurrentQuantity, item.ReorderThreshold.Amount)),
                item.AverageUnitCost))
            .OrderBy(item => item.IngredientName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <param name="Cost">Coût d'un ingrédient pour une unité vendue ; null si son coût n'est pas connu dans l'établissement.</param>
public sealed record RecipeCostLineDto(Guid IngredientId, string? IngredientName, decimal Quantity, UnitOfMeasure Unit, decimal? Cost);

/// <param name="Cost">Coût matière d'une unité vendue (devise du restaurant), somme des ingrédients chiffrés.</param>
/// <param name="IsComplete">Faux si un ingrédient n'a pas de coût connu (ou pas de stock) dans l'établissement.</param>
public sealed record RecipeCostDto(Guid RecipeId, Guid ProductId, Guid? VariantId, decimal Cost, bool IsComplete, IReadOnlyList<RecipeCostLineDto> Lines);

/// <summary>Coût matière de chaque recette avec les coûts moyens des stocks d'un établissement.</summary>
public sealed record GetRecipeCostsQuery(Guid LocationId) : IQuery<IReadOnlyList<RecipeCostDto>>;

public sealed class GetRecipeCostsHandler(InventoryWorkspace workspace) : IQueryHandler<GetRecipeCostsQuery, IReadOnlyList<RecipeCostDto>>
{
    public async Task<IReadOnlyList<RecipeCostDto>> HandleAsync(GetRecipeCostsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var tenant = workspace.CurrentTenant;
        var recipes = await workspace.Recipes.ListAsync(tenant, cancellationToken);
        var items = (await workspace.Items.ListAsync(tenant, LocationId.From(query.LocationId), lowStockOnly: false, cancellationToken))
            .ToDictionary(item => item.IngredientId);
        var names = await IngredientNames.OfAsync(
            workspace, tenant, recipes.SelectMany(recipe => recipe.Ingredients.Select(component => component.IngredientId)).Distinct(), cancellationToken);

        return recipes.Select(recipe =>
        {
            var lines = recipe.Ingredients.Select(component =>
            {
                decimal? cost = items.TryGetValue(component.IngredientId, out var item) && item.AverageUnitCost is { } unitCost
                    && component.Quantity.IsCompatibleWith(item.Unit)
                    ? decimal.Round(component.Quantity.ConvertTo(item.Unit).Amount * unitCost, 2)
                    : null;
                return new RecipeCostLineDto(component.IngredientId.Value, names.GetValueOrDefault(component.IngredientId), component.Quantity.Amount, component.Quantity.Unit, cost);
            }).ToList();
            return new RecipeCostDto(
                recipe.Id.Value, recipe.ProductId.Value, recipe.VariantId?.Value, lines.Sum(line => line.Cost ?? 0m), lines.All(line => line.Cost is not null), lines);
        }).ToList();
    }
}

internal static class PurchaseLines
{
    public static async Task<IReadOnlyList<(InventoryItem Item, Quantity Quantity, decimal? UnitPrice)>> ResolveAsync(
        InventoryWorkspace workspace, TenantId tenant, IReadOnlyList<PurchaseLineInput>? lines, CancellationToken cancellationToken)
    {
        var resolved = new List<(InventoryItem, Quantity, decimal?)>();
        foreach (var line in lines ?? [])
        {
            var item = await workspace.LoadItemAsync(tenant, line.ItemId, cancellationToken);
            resolved.Add((item, QuantityInput.Require(line.Quantity, item.Unit), line.UnitPrice));
        }

        return resolved;
    }

    public static async Task<IReadOnlyList<PurchaseOrderDto>> DescribeAsync(
        InventoryWorkspace workspace, ISupplierRepository suppliers, TenantId tenant, IReadOnlyList<PurchaseOrder> orders, CancellationToken cancellationToken)
    {
        var supplierNames = (await suppliers.ListAsync(tenant, cancellationToken)).ToDictionary(supplier => supplier.Id, supplier => supplier.Name);
        var names = await IngredientNames.OfAsync(workspace, tenant, orders.SelectMany(order => order.Lines.Select(line => line.IngredientId)).Distinct(), cancellationToken);
        return orders.Select(order => new PurchaseOrderDto(
            order.Id.Value,
            order.Number,
            order.LocationId.Value,
            order.SupplierId.Value,
            supplierNames.GetValueOrDefault(order.SupplierId),
            order.Status,
            order.Notes,
            order.EstimatedTotal,
            order.Lines.Select(line => new PurchaseOrderLineDto(
                line.ItemId.Value, line.IngredientId.Value, names.GetValueOrDefault(line.IngredientId), line.Unit, line.Ordered, line.UnitPrice, line.Received)).ToList(),
            order.CreatedAtUtc,
            order.SentAtUtc,
            order.ReceivedAtUtc)).ToList();
    }
}

internal static class PurchasingRepositories
{
    public static async Task<Supplier> LoadAsync(this ISupplierRepository suppliers, TenantId tenant, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await suppliers.FindAsync(tenant, SupplierId.From(id), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.SupplierNotFound);

    public static async Task<PurchaseOrder> LoadAsync(this IPurchaseOrderRepository orders, TenantId tenant, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await orders.FindAsync(tenant, PurchaseOrderId.From(id), cancellationToken))
        ?? throw new NotFoundException(InventoryApplicationErrors.PurchaseOrderNotFound);
}

using System.Text.Json;
using Catalog.Application.Queries;
using Inventory.Application.Abstractions;
using Inventory.Application.Commands;
using Locations.Application.Queries;
using Orders.Application.Queries;
using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Inventory, servis par les cas d'usage des autres modules, et la consommation du stock au début
// de la préparation d'une commande (par l'outbox). Aucun module n'en référence un autre : seul le host les relie. Les
// queries de Locations, Catalog et Orders répondent pour le tenant du contexte (requête ou BackgroundTenant).

/// <summary>Inventory -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class InventoryTenantsFromTenants(CachedTenants tenants) : IInventoryTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Inventory -> Locations : un établissement du tenant du contexte.</summary>
internal sealed class InventoryLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IInventoryLocations
{
    public async Task<Inventory.Domain.LocationReference?> FindAsync(
        TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new Inventory.Domain.LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Inventory -> Catalog : le produit d'une recette et ses variantes.</summary>
internal sealed class InventoryProductsFromCatalog(
    ITenantContext tenantContext, IQueryHandler<GetProductByIdQuery, ProductDto> products) : IInventoryProducts
{
    public async Task<InventoryProduct?> FindAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var product = await products.HandleAsync(new GetProductByIdQuery(productId.Value), cancellationToken);
            return new InventoryProduct(
                new Inventory.Domain.ProductReference(tenantId, productId),
                product.Variants.Select(variant => VariantId.From(variant.Id)).ToList());
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Inventory -> Orders : les lignes d'une commande (sans prix), par <see cref="GetOrderLinesQuery"/>.</summary>
internal sealed class InventoryOrdersFromOrders(
    ITenantContext tenantContext, IQueryHandler<GetOrderLinesQuery, OrderLinesDto> orders) : IInventoryOrders
{
    public async Task<InventoryOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        OrderLinesDto order;
        try
        {
            order = await orders.HandleAsync(new GetOrderLinesQuery(orderId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var hasStartedPreparation = order.Status is OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed;
        return new InventoryOrder(
            OrderId.From(order.OrderId),
            LocationId.From(order.LocationId),
            hasStartedPreparation,
            order.Lines
                .Select(line => new InventoryOrderLine(
                    ProductId.From(line.ProductId),
                    line.VariantId is { } variant ? VariantId.From(variant) : null,
                    line.Quantity))
                .ToList());
    }
}

/// <summary>
/// orders.OrderPreparationStarted -> consommation du stock de la commande d'après les recettes (idempotente : une fois
/// par commande), par l'outbox (après commit, au moins une fois). Le message est traité dans son propre scope DI, pour le
/// tenant du message (<see cref="BackgroundTenant"/>). Un refus métier part en file morte ; les autres erreurs sont
/// retentées.
/// </summary>
internal sealed class InventoryOrderFlow(IServiceScopeFactory scopes, ILogger<InventoryOrderFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("orders", "OrderPreparationStarted"))
        {
            return;
        }

        var started = message.Read<SaasRestaurant.Contracts.Orders.OrderPreparationStarted>();
        var tenantId = TenantId.From(started.TenantId);
        var orderId = started.OrderId;

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var result = await services.GetRequiredService<ICommandHandler<ConsumeStockForOrderCommand, OrderConsumptionDto>>()
            .HandleAsync(new ConsumeStockForOrderCommand(orderId), cancellationToken);
        if (result.ProductsWithoutRecipe.Count > 0 || result.IngredientsWithoutStockItem.Count > 0)
        {
            logger.LogInformation(
                "Order {OrderId}: {Products} product(s) without recipe, {Ingredients} ingredient(s) without stock item at its location.",
                orderId, result.ProductsWithoutRecipe.Count, result.IngredientsWithoutStockItem.Count);
        }
    }
}

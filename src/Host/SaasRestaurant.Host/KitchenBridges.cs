using Catalog.Application.Queries;
using Kitchen.Application.Abstractions;
using Kitchen.Application.Commands;
using Locations.Application.Queries;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Kitchen, servis par les cas d'usage des autres modules, et le flux Orders <-> Kitchen par
// l'outbox. Aucun module n'en référence un autre : seul le host les relie. Les queries de Locations, Catalog et Orders
// répondent pour le tenant du contexte (requête ou BackgroundTenant) : jamais pour un autre.

/// <summary>Kitchen -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class KitchenTenantsFromTenants(CachedTenants tenants) : IKitchenTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Kitchen -> Locations : un établissement du tenant du contexte.</summary>
internal sealed class KitchenLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IKitchenLocations
{
    public async Task<Kitchen.Domain.LocationReference?> FindAsync(
        TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new Kitchen.Domain.LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Kitchen -> Catalog : le produit d'une route de poste existe-t-il ?</summary>
internal sealed class KitchenProductsFromCatalog(
    ITenantContext tenantContext, IQueryHandler<GetProductByIdQuery, ProductDto> products) : IKitchenProducts
{
    public async Task<bool> ExistsAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        try
        {
            await products.HandleAsync(new GetProductByIdQuery(productId.Value), cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}

/// <summary>Kitchen -> Orders : les lignes à préparer d'une commande (sans prix), par <see cref="GetOrderLinesQuery"/>.</summary>
internal sealed class KitchenOrdersFromOrders(
    ITenantContext tenantContext, IQueryHandler<GetOrderLinesQuery, OrderLinesDto> orders) : IKitchenOrders
{
    public async Task<KitchenOrderToPrepare?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
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

        var isAccepted = order.Status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed;
        return new KitchenOrderToPrepare(
            OrderId.From(order.OrderId),
            LocationId.From(order.LocationId),
            isAccepted,
            order.Lines.Select(line => new KitchenOrderLineToPrepare(
                OrderItemId.From(line.OrderItemId),
                ProductId.From(line.ProductId),
                line.ProductName,
                line.VariantName,
                line.Quantity,
                line.ModifierNames,
                KitchenNote(line))).ToList(),
            order.Type == OrderType.DineIn);
    }

    /// <summary>Un composant de formule porte le nom de sa formule devant la note, pour que la cuisine regroupe le plateau.</summary>
    internal static string? KitchenNote(OrderLineDto line)
    {
        if (line.FormulaName is null)
        {
            return line.Note;
        }

        var note = string.IsNullOrWhiteSpace(line.Note) ? line.FormulaName : $"{line.FormulaName} · {line.Note}";
        return note.Length <= 200 ? note : note[..200];
    }
}

/// <summary>
/// Le flux Orders <-> Kitchen, par l'outbox (après commit, au moins une fois, dans l'ordre par module) :
/// <list type="bullet">
/// <item>orders.OrderAccepted -> ticket cuisine créé (idempotent) ;</item>
/// <item>orders.OrderCancelled -> ticket cuisine annulé (sans effet s'il n'existe pas ou est déjà clos) ;</item>
/// <item>kitchen.KitchenPreparationStarted -> commande en préparation ;</item>
/// <item>kitchen.KitchenTicketReady -> commande prête (passée d'abord en préparation si besoin).</item>
/// </list>
/// Chaque message est traité dans son propre scope DI, qui agit pour le tenant du message (<see cref="BackgroundTenant"/>).
/// Un refus métier part en file morte ; les autres erreurs sont retentées.
/// </summary>
internal sealed class KitchenOrderFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var handled = (message.Module, message.Type) is
            ("orders", "OrderAccepted" or "OrderCancelled") or ("kitchen", "KitchenPreparationStarted" or "KitchenTicketReady");
        if (!handled)
        {
            return;
        }

        var (tenantGuid, orderId, reason) = message.Type switch
        {
            "OrderAccepted" => Of(message.Read<SaasRestaurant.Contracts.Orders.OrderAccepted>()),
            "OrderCancelled" => Of(message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>()),
            "KitchenPreparationStarted" => Of(message.Read<SaasRestaurant.Contracts.Kitchen.KitchenPreparationStarted>()),
            _ => Of(message.Read<SaasRestaurant.Contracts.Kitchen.KitchenTicketReady>()),
        };
        var tenantId = TenantId.From(tenantGuid);

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        switch (message.Type)
        {
            case "OrderAccepted":
                await services.GetRequiredService<ICommandHandler<CreateTicketForOrderCommand, Guid>>()
                    .HandleAsync(new CreateTicketForOrderCommand(orderId), cancellationToken);
                break;

            case "OrderCancelled":
                await services.GetRequiredService<ICommandHandler<CancelTicketForOrderCommand>>()
                    .HandleAsync(new CancelTicketForOrderCommand(orderId, reason), cancellationToken);
                break;

            case "KitchenPreparationStarted":
                if (await StatusOfAsync(services, orderId, cancellationToken) == OrderStatus.Accepted)
                {
                    await StartPreparationAsync(services, orderId, cancellationToken);
                }

                break;

            case "KitchenTicketReady":
                await MarkReadyAsync(services, orderId, cancellationToken);
                break;
        }
    }

    private static (Guid TenantId, Guid OrderId, string? Reason) Of(SaasRestaurant.Contracts.Orders.OrderAccepted accepted) =>
        (accepted.TenantId, accepted.OrderId, null);

    private static (Guid TenantId, Guid OrderId, string? Reason) Of(SaasRestaurant.Contracts.Orders.OrderCancelled cancelled) =>
        (cancelled.TenantId, cancelled.OrderId, cancelled.Reason);

    private static (Guid TenantId, Guid OrderId, string? Reason) Of(SaasRestaurant.Contracts.Kitchen.KitchenPreparationStarted started) =>
        (started.TenantId, started.OrderId, null);

    private static (Guid TenantId, Guid OrderId, string? Reason) Of(SaasRestaurant.Contracts.Kitchen.KitchenTicketReady ready) =>
        (ready.TenantId, ready.OrderId, null);

    // Le ticket peut être prêt sans que le début de préparation ait atteint la commande (lignes marquées prêtes
    // directement) : la commande passe alors par « en préparation » avant « prête ». Une commande déjà prête, terminée
    // ou annulée n'est pas touchée (message reçu deux fois, ou commande avancée à la main).
    private static async Task MarkReadyAsync(IServiceProvider services, Guid orderId, CancellationToken cancellationToken)
    {
        var status = await StatusOfAsync(services, orderId, cancellationToken);
        if (status == OrderStatus.Accepted)
        {
            await StartPreparationAsync(services, orderId, cancellationToken);
            status = OrderStatus.Preparing;
        }

        if (status == OrderStatus.Preparing)
        {
            await services.GetRequiredService<ICommandHandler<MarkOrderReadyCommand>>()
                .HandleAsync(new MarkOrderReadyCommand(orderId), cancellationToken);
        }
    }

    private static Task StartPreparationAsync(IServiceProvider services, Guid orderId, CancellationToken cancellationToken) =>
        services.GetRequiredService<ICommandHandler<StartOrderPreparationCommand>>()
            .HandleAsync(new StartOrderPreparationCommand(orderId), cancellationToken);

    private static async Task<OrderStatus> StatusOfAsync(IServiceProvider services, Guid orderId, CancellationToken cancellationToken) =>
        (await services.GetRequiredService<IQueryHandler<GetOrderLinesQuery, OrderLinesDto>>()
            .HandleAsync(new GetOrderLinesQuery(orderId), cancellationToken)).Status;
}

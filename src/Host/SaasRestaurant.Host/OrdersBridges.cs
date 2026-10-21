using Catalog.Application.Queries;
using Floor.Application.Commands;
using Floor.Application.Queries;
using Locations.Application.Queries;
using Orders.Application.Abstractions;
using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Orders, servis par les cas d'usage des autres modules, et la projection des commandes de session
// tenue par Floor (par l'outbox). Aucun module n'en référence un autre : seul le host les relie. Les queries de
// Locations, Floor et Catalog répondent pour le tenant de la requête : jamais pour un autre.

/// <summary>Orders -> Tenants : devise, mode de prix (TTC/HT) et écritures autorisées.</summary>
internal sealed class OrderTenantsFromTenants(CachedTenants tenants) : IOrderTenants
{
    public async Task<OrderTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        return tenant is null
            ? null
            : new OrderTenant(
                Currency.FromCode(tenant.DefaultCurrencyCode),
                tenant.PricesIncludeTax ? PriceTaxMode.TaxInclusive : PriceTaxMode.TaxExclusive,
                tenant.CanAcceptBusinessWrites);
    }
}

/// <summary>Orders -> Locations : l'établissement de la commande (opérationnel ?).</summary>
internal sealed class OrderLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IOrderLocations
{
    public async Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Orders -> Floor : la session de table d'une commande sur place.</summary>
internal sealed class OrderTableSessionsFromFloor(
    ITenantContext tenantContext, IQueryHandler<GetTableSessionByIdQuery, TableSessionDto> sessions) : IOrderTableSessions
{
    public async Task<TableSessionReference?> FindAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var session = await sessions.HandleAsync(new GetTableSessionByIdQuery(sessionId.Value), cancellationToken);
            return new TableSessionReference(tenantId, sessionId, LocationId.From(session.LocationId), session.IsOpen);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Orders -> Catalog : l'instantané d'une ligne (produit actif, variante, options valides, prix, taxe). Les refus du
/// Catalog (produit non vendable, choix d'options invalide) remontent tels quels ; un produit inconnu donne null.
/// </summary>
internal sealed class OrderCatalogFromCatalog(
    ITenantContext tenantContext, IQueryHandler<GetSellableItemQuery, SellableItemDto> sellable) : IOrderCatalog
{
    public async Task<ProductSnapshot?> DescribeForSaleAsync(
        TenantId tenantId,
        ProductId productId,
        VariantId? variantId,
        IReadOnlyList<ModifierId> modifierIds,
        LocationId locationId,
        OrderChannel channel,
        IReadOnlyList<ComponentChoice>? components = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modifierIds);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        SellableItemDto item;
        try
        {
            item = await sellable.HandleAsync(
                new GetSellableItemQuery(
                    productId.Value,
                    variantId?.Value,
                    modifierIds.Select(id => id.Value).ToList(),
                    locationId.Value,
                    PriceChannelOf(channel),
                    (components ?? [])
                        .Select(component => new SaleComponentInput(
                            component.StepId, component.ProductId.Value, component.VariantId?.Value, component.ModifierIds.Select(id => id.Value).ToList()))
                        .ToList()),
                cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var currency = Currency.FromCode(item.CurrencyCode);
        return ProductSnapshot.Create(
            tenantId,
            ProductId.From(item.ProductId),
            item.ProductName,
            Money.Of(item.UnitPrice, currency),
            TaxSnapshot.Create(item.TaxCategoryCode, item.TaxCategoryName, item.TaxRate),
            item.Modifiers.Select(modifier =>
                OrderItemModifier.Create(ModifierId.From(modifier.ModifierId), modifier.Name, Money.Of(modifier.PriceDelta, currency))),
            item.VariantId is { } variant ? VariantId.From(variant) : null,
            item.VariantName,
            (item.Components ?? []).Select(component => OrderItemComponent.Create(
                null,
                component.StepName,
                ProductId.From(component.ProductId),
                component.ProductName,
                component.VariantId is { } choice ? VariantId.From(choice) : null,
                component.VariantName,
                Money.Of(component.Supplement, currency),
                component.Modifiers.Select(modifier =>
                    OrderItemModifier.Create(ModifierId.From(modifier.ModifierId), modifier.Name, Money.Of(modifier.PriceDelta, currency))))));
    }

    /// <summary>
    /// Le canal de prix d'une commande : la salle, le comptoir, le téléphone et WhatsApp au prix de la maison ; le QR de
    /// table au prix du site, comme le panier qui l'a chiffré.
    /// </summary>
    internal static Catalog.Domain.PriceChannel PriceChannelOf(OrderChannel channel) => channel switch
    {
        OrderChannel.Web or OrderChannel.QrCode => Catalog.Domain.PriceChannel.Web,
        OrderChannel.MobileApp => Catalog.Domain.PriceChannel.MobileApp,
        OrderChannel.Kiosk => Catalog.Domain.PriceChannel.Kiosk,
        _ => Catalog.Domain.PriceChannel.InHouse,
    };
}

internal sealed class SessionOrderFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "orders" || message.Type is not ("OrderCreated" or "OrderTableSessionChanged" or "OrderCompleted" or "OrderCancelled"))
        {
            return;
        }

        var (tenantId, command) = message.Type switch
        {
            "OrderCreated" => Opened(message.Read<SaasRestaurant.Contracts.Orders.OrderCreated>()),
            "OrderTableSessionChanged" => Moved(message.Read<SaasRestaurant.Contracts.Orders.OrderTableSessionChanged>()),
            "OrderCompleted" => Closed(message.Read<SaasRestaurant.Contracts.Orders.OrderCompleted>()),
            _ => Closed(message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>()),
        };
        if (command is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
        switch (command)
        {
            case RecordSessionOrderOpenedCommand opened:
                await services.GetRequiredService<ICommandHandler<RecordSessionOrderOpenedCommand>>().HandleAsync(opened, cancellationToken);
                break;
            case RecordSessionOrderClosedCommand closed:
                await services.GetRequiredService<ICommandHandler<RecordSessionOrderClosedCommand>>().HandleAsync(closed, cancellationToken);
                break;
        }
    }

    private static (Guid, object?) Opened(SaasRestaurant.Contracts.Orders.OrderCreated created) =>
        (created.TenantId, created.TableSessionId is { } session ? new RecordSessionOrderOpenedCommand(created.OrderId, session) : null);

    // Commande changée de table : la nouvelle session la compte comme ouverte, l'ancienne plus.
    private static (Guid, object?) Moved(SaasRestaurant.Contracts.Orders.OrderTableSessionChanged moved) =>
        (moved.TenantId, new RecordSessionOrderOpenedCommand(moved.OrderId, moved.TableSessionId));

    private static (Guid, object?) Closed(SaasRestaurant.Contracts.Orders.OrderCompleted completed) =>
        (completed.TenantId, new RecordSessionOrderClosedCommand(completed.OrderId));

    private static (Guid, object?) Closed(SaasRestaurant.Contracts.Orders.OrderCancelled cancelled) =>
        (cancelled.TenantId, new RecordSessionOrderClosedCommand(cancelled.OrderId));
}

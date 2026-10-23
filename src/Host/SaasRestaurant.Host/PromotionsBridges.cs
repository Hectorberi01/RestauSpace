using Catalog.Application.Queries;
using Locations.Application.Queries;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using Promotions.Application.Abstractions;
using Promotions.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Promotions, servis par les cas d'usage des autres modules, et les réactions des promotions aux
// faits des commandes (par l'outbox). Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Promotions -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class PromotionTenantsFromTenants(CachedTenants tenants) : IPromotionTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Promotions -> Locations : un établissement ciblé par une promotion est-il du tenant du contexte ?</summary>
internal sealed class PromotionLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IPromotionLocations
{
    public async Task<bool> ExistsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        try
        {
            await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}

/// <summary>Promotions -> Catalog : un produit ciblé par une promotion est-il du tenant du contexte ?</summary>
internal sealed class PromotionProductsFromCatalog(
    ITenantContext tenantContext, IQueryHandler<GetProductByIdQuery, ProductDto> products) : IPromotionProducts
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

/// <summary>
/// Promotions -> Orders : établissement, canal, devise, lignes (total de ligne dans le mode de prix de la commande, avant
/// remise) et état d'une commande du tenant du contexte. La variante d'une ligne est ignorée : une promotion vaut pour
/// toutes les variantes d'un produit.
/// </summary>
internal sealed class PromotionOrdersFromOrders(ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : IPromotionOrders
{
    public async Task<PromotionOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        OrderDto order;
        try
        {
            order = await orders.HandleAsync(new GetOrderByIdQuery(orderId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        return new PromotionOrder(
            orderId,
            LocationId.From(order.LocationId),
            ToPromotionOrderType(order.Type),
            order.CurrencyCode,
            order.Status == OrderStatus.Draft,
            order.Items.Select(item => new PromotionOrderLine(ProductId.From(item.ProductId), item.LineTotal)).ToList());
    }

    /// <summary>Les deux enums portent les mêmes valeurs ; une valeur inconnue d'un côté est une erreur de version.</summary>
    private static Promotions.Domain.OrderType ToPromotionOrderType(Orders.Domain.OrderType type) =>
        type switch
        {
            Orders.Domain.OrderType.DineIn => Promotions.Domain.OrderType.DineIn,
            Orders.Domain.OrderType.Takeaway => Promotions.Domain.OrderType.Takeaway,
            Orders.Domain.OrderType.Delivery => Promotions.Domain.OrderType.Delivery,
            _ => throw new InvalidOperationException("Unknown order type " + type + "."),
        };
}

/// <summary>
/// Réactions des promotions, par l'outbox (après commit, au moins une fois), chacune dans son propre scope DI pour le
/// tenant du message (<see cref="BackgroundTenant"/>) :
/// <list type="bullet">
/// <item>promotions.PromotionApplied -> remise posée sur la commande (source Promotion, référence = id de la promotion) ;
/// si la commande ne l'accepte plus, l'usage est rendu (compensation) ;</item>
/// <item>orders.OrderCancelled -> usages de promotion et de coupon de la commande rendus.</item>
/// </list>
/// Les réactions sont idempotentes. Un refus métier part en file morte ; les autres erreurs sont retentées. Journal :
/// ids et codes seulement.
/// </summary>
internal sealed class PromotionsFlow(IServiceScopeFactory scopes, ILogger<PromotionsFlow> logger) : IOutboxConsumer
{
    private const string PromotionDiscountLabel = "Promotion";

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (("orders", "OrderCancelled") or ("promotions", "PromotionApplied")))
        {
            return;
        }

        var applied = message.Type == "PromotionApplied" ? message.Read<SaasRestaurant.Contracts.Promotions.PromotionApplied>() : null;
        var cancelled = applied is null ? message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>() : null;
        var tenantId = TenantId.From(applied?.TenantId ?? cancelled!.TenantId);
        var orderId = applied?.OrderId ?? cancelled!.OrderId;

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var outcome = applied is null
            ? await services.GetRequiredService<ICommandHandler<ReleaseOrderPromotionsCommand, PromotionReactionOutcome>>()
                .HandleAsync(new ReleaseOrderPromotionsCommand(orderId), cancellationToken)
            : await ApplyDiscountAsync(services, applied, orderId, cancellationToken);

        if (outcome == PromotionReactionOutcome.Done)
        {
            logger.LogInformation("Outbox message {Module}/{MessageId} ({Type}) applied to promotions.", message.Module, message.Id, message.Type);
        }
    }

    /// <summary>Pose la remise ; déjà posée (rejeu) = rien à faire ; refusée par la commande = usage rendu.</summary>
    private async Task<PromotionReactionOutcome> ApplyDiscountAsync(
        IServiceProvider services, SaasRestaurant.Contracts.Promotions.PromotionApplied applied, Guid orderId, CancellationToken cancellationToken)
    {
        var promotionId = applied.PromotionId;
        var discount = applied.Discount;
        try
        {
            await services.GetRequiredService<ICommandHandler<ApplyExternalDiscountCommand>>().HandleAsync(
                new ApplyExternalDiscountCommand(
                    orderId,
                    DiscountSource.Promotion,
                    discount.Amount,
                    discount.Currency,
                    promotionId.ToString(),
                    PromotionDiscountLabel),
                cancellationToken);
            return PromotionReactionOutcome.Done;
        }
        catch (DomainException exception) when (exception.Code == OrderErrors.DiscountAlreadyApplied.Code)
        {
            return PromotionReactionOutcome.AlreadyDone;
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException)
        {
            logger.LogWarning(
                "Promotion discount refused by order {OrderId}: {Code}; giving the usage back.",
                orderId, (exception as DomainException)?.Code ?? exception.GetType().Name);
            return await services.GetRequiredService<ICommandHandler<RevertPromotionForOrderCommand, PromotionReactionOutcome>>()
                .HandleAsync(new RevertPromotionForOrderCommand(orderId, promotionId), cancellationToken);
        }
    }
}

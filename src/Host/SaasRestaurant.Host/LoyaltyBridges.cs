using Loyalty.Application.Abstractions;
using Loyalty.Application.Commands;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Loyalty, servis par les cas d'usage des autres modules, et les réactions de la fidélité aux
// faits des commandes et des clients (par l'outbox). Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Loyalty -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class LoyaltyTenantsFromTenants(CachedTenants tenants) : ILoyaltyTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Loyalty -> Customers : (tenant, id, anonymisé) d'un client du tenant du contexte, rien d'autre.</summary>
internal sealed class LoyaltyCustomersFromCustomers(CustomerReferences customers) : ILoyaltyCustomers
{
    public async Task<Loyalty.Domain.CustomerReference?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await customers.FindAsync(tenantId, customerId, cancellationToken) is { } reference
            ? new Loyalty.Domain.CustomerReference(tenantId, customerId, reference.IsAnonymized)
            : null;
}

/// <summary>Loyalty -> Orders : le client, la devise et l'état d'une commande du tenant du contexte.</summary>
internal sealed class LoyaltyOrdersFromOrders(ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : ILoyaltyOrders
{
    public async Task<LoyaltyOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var order = await orders.HandleAsync(new GetOrderByIdQuery(orderId.Value), cancellationToken);
            return new LoyaltyOrder(
                orderId,
                order.CustomerId is { } customer ? CustomerId.From(customer) : null,
                order.CurrencyCode,
                order.Status == OrderStatus.Draft);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Réactions de la fidélité, par l'outbox (après commit, au moins une fois), chacune dans son propre scope DI pour le
/// tenant du message (<see cref="BackgroundTenant"/>) :
/// <list type="bullet">
/// <item>orders.OrderCompleted -> points gagnés selon le barème (inscription automatique si prévue) ;</item>
/// <item>orders.OrderCancelled -> points de la commande repris, récompense utilisée dessus rendue ;</item>
/// <item>customers.CustomerAnonymized -> compte du client clos ;</item>
/// <item>loyalty.RewardRedeemed -> remise de la récompense posée sur la commande ; si la commande ne l'accepte plus,
/// les points sont rendus (compensation).</item>
/// </list>
/// Les réactions sont idempotentes. Un refus métier part en file morte ; les autres erreurs sont retentées. Journal :
/// ids et codes seulement.
/// </summary>
internal sealed class LoyaltyFlow(IServiceScopeFactory scopes, ILogger<LoyaltyFlow> logger) : IOutboxConsumer
{
    private const string RewardDiscountLabel = "Récompense fidélité";

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (("orders", "OrderCompleted") or ("orders", "OrderCancelled")
            or ("customers", "CustomerAnonymized") or ("loyalty", "RewardRedeemed")))
        {
            return;
        }

        var tenantId = TenantId.From(message.Type switch
        {
            "OrderCompleted" => message.Read<SaasRestaurant.Contracts.Orders.OrderCompleted>().TenantId,
            "OrderCancelled" => message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>().TenantId,
            "CustomerAnonymized" => message.Read<SaasRestaurant.Contracts.Customers.CustomerAnonymized>().TenantId,
            _ => message.Read<SaasRestaurant.Contracts.Loyalty.RewardRedeemed>().TenantId,
        });

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var outcome = message.Type switch
        {
            "OrderCompleted" => await EarnAsync(services, message.Read<SaasRestaurant.Contracts.Orders.OrderCompleted>(), cancellationToken),
            "OrderCancelled" => await ReverseAsync(services, message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>(), cancellationToken),
            "CustomerAnonymized" => await services.GetRequiredService<ICommandHandler<CloseCustomerLoyaltyAccountCommand, LoyaltyReactionOutcome>>()
                .HandleAsync(
                    new CloseCustomerLoyaltyAccountCommand(message.Read<SaasRestaurant.Contracts.Customers.CustomerAnonymized>().CustomerId),
                    cancellationToken),
            _ => await ApplyRewardDiscountAsync(services, message.Read<SaasRestaurant.Contracts.Loyalty.RewardRedeemed>(), cancellationToken),
        };

        if (outcome == LoyaltyReactionOutcome.Done)
        {
            logger.LogInformation("Outbox message {Module}/{MessageId} ({Type}) applied to loyalty.", message.Module, message.Id, message.Type);
        }
    }

    /// <summary>
    /// Pose la remise de la récompense sur la commande (référence de l'échange = id de la commande). Déjà posée (rejeu) =
    /// rien à faire. Refusée par la commande (plus en brouillon, devise...) = les points de l'échange sont rendus.
    /// </summary>
    private static Task<LoyaltyReactionOutcome> EarnAsync(
        IServiceProvider services, SaasRestaurant.Contracts.Orders.OrderCompleted completed, CancellationToken cancellationToken) =>
        services.GetRequiredService<ICommandHandler<EarnPointsForOrderCommand, LoyaltyReactionOutcome>>().HandleAsync(
            new EarnPointsForOrderCommand(completed.OrderId, completed.CustomerId, completed.Total.Amount, completed.Total.Currency),
            cancellationToken);

    private static Task<LoyaltyReactionOutcome> ReverseAsync(
        IServiceProvider services, SaasRestaurant.Contracts.Orders.OrderCancelled cancelled, CancellationToken cancellationToken) =>
        services.GetRequiredService<ICommandHandler<ReverseOrderLoyaltyCommand, LoyaltyReactionOutcome>>()
            .HandleAsync(new ReverseOrderLoyaltyCommand(cancelled.OrderId, cancelled.CustomerId), cancellationToken);

    private async Task<LoyaltyReactionOutcome> ApplyRewardDiscountAsync(
        IServiceProvider services, SaasRestaurant.Contracts.Loyalty.RewardRedeemed redeemed, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(redeemed.Reference, out var orderId))
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var discount = redeemed.DiscountValue;
        var accountId = redeemed.LoyaltyAccountId;
        try
        {
            await services.GetRequiredService<ICommandHandler<ApplyExternalDiscountCommand>>().HandleAsync(
                new ApplyExternalDiscountCommand(
                    orderId,
                    DiscountSource.LoyaltyReward,
                    discount.Amount,
                    discount.Currency,
                    redeemed.DiscountReference,
                    RewardDiscountLabel),
                cancellationToken);
            return LoyaltyReactionOutcome.Done;
        }
        catch (DomainException exception) when (exception.Code == OrderErrors.DiscountAlreadyApplied.Code)
        {
            return LoyaltyReactionOutcome.AlreadyDone;
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException)
        {
            logger.LogWarning(
                "Loyalty reward discount refused by order {OrderId}: {Code}; giving the points back.",
                orderId, (exception as DomainException)?.Code ?? exception.GetType().Name);
            return await services.GetRequiredService<ICommandHandler<CancelRewardRedemptionCommand, LoyaltyReactionOutcome>>()
                .HandleAsync(new CancelRewardRedemptionCommand(accountId, orderId), cancellationToken);
        }
    }
}

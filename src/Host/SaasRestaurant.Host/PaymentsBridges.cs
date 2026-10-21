using Locations.Application.Queries;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;
using Payments.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Payments, servis par les cas d'usage des autres modules, et la projection des règlements tenue
// par Orders (par l'outbox). Aucun module n'en référence un autre : seul le host les relie. Les queries d'Orders
// répondent pour le tenant du contexte (requête ou BackgroundTenant) : jamais pour un autre.

/// <summary>Payments -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class PaymentTenantsFromTenants(CachedTenants tenants) : IPaymentTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Payments -> Tenants : la devise du restaurant (celle de sa caisse) ; null s'il est inconnu.</summary>
internal sealed class PaymentTenantCurrencyFromTenants(CachedTenants tenants) : IPaymentTenantCurrency
{
    public async Task<Currency?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is { } tenant ? Currency.FromCode(tenant.DefaultCurrencyCode) : null;
}

/// <summary>Payments -> Locations : l'établissement du tenant courant et son pays ; null ailleurs ou s'il n'existe pas.</summary>
internal sealed class PaymentLocationsFromLocations(ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations)
    : IPaymentLocations
{
    public async Task<Payments.Domain.LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new Payments.Domain.LocationReference(tenantId, locationId, Payments.Domain.CountryCode.Create(location.Address.CountryCode));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Payments -> Orders : total et devise de la commande, et si elle peut encore être payée.</summary>
internal sealed class PaymentOrdersFromOrders(
    ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : IPaymentOrders
{
    public async Task<PaymentOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
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

        // Payable une fois passée, tant qu'elle n'est ni terminée ni annulée (un brouillon se compose encore).
        var acceptsPayments = order.Status is OrderStatus.Placed or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready;
        var currency = Currency.FromCode(order.CurrencyCode);
        return new PaymentOrder(
            OrderId.From(order.Id),
            LocationId.From(order.LocationId),
            Money.Of(order.Total, currency),
            acceptsPayments,
            order.Items.Select(item => new PaymentOrderLine(item.Id, item.Quantity, Money.Of(item.TotalWithTax, currency))).ToList());
    }
}

internal sealed class OrderSettlementFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("payments", "PaymentSettlementChanged"))
        {
            return;
        }

        var changed = message.Read<SaasRestaurant.Contracts.Payments.PaymentSettlementChanged>();
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(changed.TenantId));
        await services.GetRequiredService<ICommandHandler<RecordPaymentSettlementCommand>>().HandleAsync(
            new RecordPaymentSettlementCommand(
                changed.PaymentId, changed.OrderId, changed.SettledAmount.Amount, changed.SettledAmount.Currency, message.Id),
            cancellationToken);
    }
}

/// <summary>
/// Payments -> Loyalty : cartes cadeaux et avoirs du tenant du contexte. Le code n'est passé qu'au débit et ne
/// ressort jamais (seulement l'id de la carte et ses 4 derniers caractères), sauf à l'émission d'un avoir.
/// </summary>
internal sealed class PaymentStoredValueFromLoyalty(
    ITenantContext tenantContext,
    ICommandHandler<Loyalty.Application.Commands.RedeemStoredValueCommand, Loyalty.Application.Commands.StoredValueRedemptionDto> redeem,
    ICommandHandler<Loyalty.Application.Commands.CreditStoredValueCommand> credit,
    ICommandHandler<Loyalty.Application.Commands.ReverseStoredValueMovementCommand> reverse,
    ICommandHandler<Loyalty.Application.Commands.IssueStoredValueCardCommand, Loyalty.Application.Commands.IssuedStoredValueCardDto> issue,
    ICommandHandler<Loyalty.Application.Commands.CancelStoredValueCardCommand> cancel) : IPaymentStoredValue
{
    public async Task<StoredValueDebit> RedeemAsync(TenantId tenantId, string code, Money amount, string reference, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        var result = await redeem.HandleAsync(
            new Loyalty.Application.Commands.RedeemStoredValueCommand(code, amount.Amount, amount.Currency.Code, reference), cancellationToken);
        return new StoredValueDebit(result.CardId, result.CodeHint);
    }

    public Task CreditAsync(TenantId tenantId, Guid cardId, Money amount, string reference, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return credit.HandleAsync(new Loyalty.Application.Commands.CreditStoredValueCommand(cardId, amount.Amount, amount.Currency.Code, reference), cancellationToken);
    }

    public Task ReverseAsync(TenantId tenantId, Guid cardId, string reference, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return reverse.HandleAsync(new Loyalty.Application.Commands.ReverseStoredValueMovementCommand(cardId, reference), cancellationToken);
    }

    public async Task<IssuedStoreCredit> IssueStoreCreditAsync(TenantId tenantId, Money amount, string note, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        var issued = await issue.HandleAsync(
            new Loyalty.Application.Commands.IssueStoredValueCardCommand(
                Loyalty.Domain.StoredValueKind.StoreCredit, amount.Amount, amount.Currency.Code, Note: note),
            cancellationToken);
        return new IssuedStoreCredit(issued.Id, issued.Code, issued.CodeHint);
    }

    public Task CancelAsync(TenantId tenantId, Guid cardId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        return cancel.HandleAsync(new Loyalty.Application.Commands.CancelStoredValueCardCommand(cardId, reason), cancellationToken);
    }

    // Les cas d'usage de Loyalty agissent pour le tenant du contexte : un autre tenant demandé est une erreur de câblage.
    private void EnsureTenant(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Stored value requested for another tenant than the current one.");
        }
    }
}

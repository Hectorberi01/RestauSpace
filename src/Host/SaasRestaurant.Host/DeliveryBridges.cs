using Delivery.Application.Abstractions;
using Delivery.Application.Commands;
using Delivery.Domain;
using Locations.Application.Queries;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Payments.Application;
using Payments.Application.Commands;
using Payments.Application.Queries;
using CashMovementKind = Payments.Domain.CashMovementKind;
using PaymentChannel = Payments.Domain.PaymentChannel;
using PaymentMethodType = Payments.Domain.PaymentMethodType;
using PaymentProviderType = Payments.Domain.PaymentProviderType;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

// Les ports déclarés par Delivery, servis par les cas d'usage des autres modules, et la répercussion des événements de
// commande sur les fiches (par l'outbox). Aucun module n'en référence un autre : seul le host les relie. Les queries
// répondent pour le tenant du contexte (requête ou BackgroundTenant) : jamais pour un autre.

/// <summary>Delivery -> Tenants : écritures métier acceptées (inconnu = non) et devise du restaurant.</summary>
internal sealed class DeliveryTenantsFromTenants(CachedTenants tenants) : IDeliveryTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<Currency> CurrencyOfAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is { } tenant
            ? Currency.FromCode(tenant.DefaultCurrencyCode)
            : throw new InvalidOperationException("Restaurant introuvable.");
}

/// <summary>Delivery -> Locations : l'établissement des réglages et des fiches.</summary>
internal sealed class DeliveryLocationsFromLocations(ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations)
    : IDeliveryLocations
{
    public async Task<Delivery.Domain.LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new Delivery.Domain.LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Delivery -> Orders : la commande réduite à (tenant, id, établissement, type, total, ouverte). Ni lignes ni client.</summary>
internal sealed class DeliveryOrdersFromOrders(ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : IDeliveryOrders
{
    public async Task<OrderReference?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var order = await orders.HandleAsync(new GetOrderByIdQuery(orderId.Value), cancellationToken);
            return new OrderReference(
                tenantId,
                orderId,
                LocationId.From(order.LocationId),
                order.Type switch
                {
                    Orders.Domain.OrderType.Takeaway => OrderKind.Takeaway,
                    Orders.Domain.OrderType.Delivery => OrderKind.Delivery,
                    _ => OrderKind.DineIn,
                },
                Money.Of(order.Total, Currency.FromCode(order.CurrencyCode)),
                order.Status is not (Orders.Domain.OrderStatus.Completed or Orders.Domain.OrderStatus.Cancelled));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// orders.OrderReady -> la fiche de retrait passe « prête à retirer » ; orders.OrderCancelled -> la fiche active est
/// annulée. Par l'outbox (après commit, au moins une fois), dans son propre scope DI pour le tenant du message ; les
/// réactions sont idempotentes. Un refus métier part en file morte ; les autres erreurs sont retentées. Journal : ids
/// seulement.
/// </summary>
internal sealed class DeliveryOrderFlow(IServiceScopeFactory scopes, ILogger<DeliveryOrderFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (("orders", "OrderReady") or ("orders", "OrderCancelled")))
        {
            return;
        }

        var (tenantId, orderId) = message.Type == "OrderReady"
            ? Ids(message.Read<SaasRestaurant.Contracts.Orders.OrderReady>())
            : Ids(message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>());

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));

        var changed = message.Type == "OrderReady"
            ? await services.GetRequiredService<ICommandHandler<MarkOrderReadyForPickupCommand, bool>>()
                .HandleAsync(new MarkOrderReadyForPickupCommand(orderId), cancellationToken)
            : await services.GetRequiredService<ICommandHandler<CancelOrderFulfillmentCommand, bool>>()
                .HandleAsync(new CancelOrderFulfillmentCommand(orderId, null), cancellationToken);

        if (changed)
        {
            logger.LogInformation("Order {OrderId} ({Type}): fulfillment updated.", orderId, message.Type);
        }
    }

    private static (Guid TenantId, Guid OrderId) Ids(SaasRestaurant.Contracts.Orders.OrderReady ready) => (ready.TenantId, ready.OrderId);

    private static (Guid TenantId, Guid OrderId) Ids(SaasRestaurant.Contracts.Orders.OrderCancelled cancelled) => (cancelled.TenantId, cancelled.OrderId);
}

/// <summary>
/// Les frais de livraison entrent dans le total de la commande : delivery.FulfillmentCreated (livraison avec frais) les
/// pose sur la commande, la fiche annulée (FulfillmentStatusChanged -> Cancelled) les retire. Une commande déjà terminée
/// ou annulée ne change plus : la réaction est alors sans effet (journalisée). Idempotent ; journal : ids et codes.
/// </summary>
internal sealed class DeliveryFeeFlow(IServiceScopeFactory scopes, ILogger<DeliveryFeeFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "delivery" || message.Type is not ("FulfillmentCreated" or "FulfillmentStatusChanged"))
        {
            return;
        }

        Guid tenantId;
        Guid orderId;
        object command;
        if (message.Type == "FulfillmentCreated")
        {
            var created = message.Read<SaasRestaurant.Contracts.Delivery.FulfillmentCreated>();
            if (created.DeliveryFee is not { } fee || fee.Amount <= 0)
            {
                return;
            }

            (tenantId, orderId) = (created.TenantId, created.OrderId);
            command = new ApplyDeliveryFeeCommand(orderId, fee.Amount, fee.Currency, created.FulfillmentId.ToString("D"));
        }
        else
        {
            var changed = message.Read<SaasRestaurant.Contracts.Delivery.FulfillmentStatusChanged>();
            if (changed.Mode != nameof(FulfillmentMode.Delivery) || changed.Status != nameof(FulfillmentStatus.Cancelled))
            {
                return;
            }

            (tenantId, orderId) = (changed.TenantId, changed.OrderId);
            command = new RemoveDeliveryFeeCommand(orderId, changed.FulfillmentId.ToString("D"));
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
        try
        {
            if (command is ApplyDeliveryFeeCommand apply)
            {
                await services.GetRequiredService<ICommandHandler<ApplyDeliveryFeeCommand>>().HandleAsync(apply, cancellationToken);
            }
            else
            {
                await services.GetRequiredService<ICommandHandler<RemoveDeliveryFeeCommand>>().HandleAsync((RemoveDeliveryFeeCommand)command, cancellationToken);
            }

            logger.LogInformation("Order {OrderId}: delivery fee updated ({Type}).", orderId, message.Type);
        }
        catch (DomainException exception) when (exception.Code == Orders.Domain.OrderErrors.Finalized.Code)
        {
            logger.LogInformation("Order {OrderId} is finalized: delivery fee left unchanged.", orderId);
        }
    }
}


/// <summary>
/// Delivery -> Payments : le reste à payer de la commande, et si l'établissement accepte l'encaissement par le livreur
/// (espèces : moyen « Espèces encaissées par le livreur » ; mobile money : le téléphone du restaurant).
/// </summary>
internal sealed class DeliveryPaymentsFromPayments(
    ITenantContext tenantContext,
    IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> orderPayments,
    IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>> available) : IDeliveryPayments
{
    public async Task<Money> AmountDueAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        var payments = await orderPayments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken);
        return Money.Of(Math.Max(payments.AmountDue, 0m), Currency.FromCode(payments.CurrencyCode));
    }

    public async Task<bool> AcceptsAsync(
        TenantId tenantId, LocationId locationId, CollectionMethod method, Money amount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amount);
        EnsureTenant(tenantId);
        return await DeliveryCollection.ProviderAsync(available, locationId.Value, method, amount.Amount, amount.Currency.Code, cancellationToken) is not null;
    }

    private void EnsureTenant(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new NotFoundException(Delivery.Application.DeliveryApplicationErrors.OrderNotFound);
        }
    }
}

internal static class DeliveryCollection
{
    /// <summary>Référence du paiement enregistré pour une livraison : une seule par fiche, ce qui rend la réaction idempotente.</summary>
    public static string Reference(Guid fulfillmentId) => "LIV-" + fulfillmentId.ToString("N");

    public static PaymentMethodType PaymentMethod(CollectionMethod method) =>
        method == CollectionMethod.Cash ? PaymentMethodType.CashOnDelivery : PaymentMethodType.MobileMoneyOnSite;

    /// <summary>Le moyen du comptoir qui enregistre cet encaissement dans l'établissement ; null s'il n'est pas accepté.</summary>
    public static async Task<string?> ProviderAsync(
        IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>> available,
        Guid locationId,
        CollectionMethod method,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var wanted = PaymentMethod(method);
        var methods = await available.HandleAsync(new GetAvailablePaymentMethodsQuery(locationId, amount, currency, PaymentChannel.InPerson), cancellationToken);
        return methods
            .Where(item => item.Method == wanted && item.Type == PaymentProviderType.OnSite)
            .OrderBy(item => item.Priority)
            .Select(item => item.Provider)
            .FirstOrDefault();
    }
}

/// <summary>
/// delivery.DeliveryPaymentCollected -> le paiement est enregistré sur la commande (espèces du livreur, ou mobile money
/// reçu sur le téléphone du restaurant), avec la référence de la fiche : un message rejoué ne paie pas deux fois. Une
/// commande déjà réglée entre-temps n'est pas payée en double (journalisé). Journal : ids seulement.
/// </summary>
internal sealed class DeliveryCollectionFlow(IServiceScopeFactory scopes, ILogger<DeliveryCollectionFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("delivery", "DeliveryPaymentCollected"))
        {
            return;
        }

        var collected = message.Read<SaasRestaurant.Contracts.Delivery.DeliveryPaymentCollected>();
        if (!Enum.TryParse<CollectionMethod>(collected.Method, out var method) || collected.Amount.Amount <= 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(collected.TenantId));

        var reference = DeliveryCollection.Reference(collected.FulfillmentId);
        var payments = await services.GetRequiredService<IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto>>()
            .HandleAsync(new GetOrderPaymentsQuery(collected.OrderId), cancellationToken);
        if (payments.Payments.Any(payment => payment.Attempts.Any(attempt => attempt.ProviderReference == reference)))
        {
            return;
        }

        if (payments.AmountDue <= 0)
        {
            logger.LogInformation("Order {OrderId}: already paid, delivery collection {FulfillmentId} not recorded.", collected.OrderId, collected.FulfillmentId);
            return;
        }

        var amount = Math.Min(collected.Amount.Amount, payments.AmountDue);
        var provider = await DeliveryCollection.ProviderAsync(
            services.GetRequiredService<IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>>>(),
            collected.LocationId,
            method,
            amount,
            collected.Amount.Currency,
            cancellationToken);
        if (provider is null)
        {
            logger.LogWarning(
                "Order {OrderId}: delivery collection {FulfillmentId} ({Method}) not accepted by the location, payment not recorded.",
                collected.OrderId,
                collected.FulfillmentId,
                method);
            return;
        }

        await services.GetRequiredService<ICommandHandler<RecordOfflinePaymentCommand, Guid>>().HandleAsync(
            new RecordOfflinePaymentCommand(collected.OrderId, provider, reference, amount, DeliveryCollection.PaymentMethod(method)), cancellationToken);
        logger.LogInformation("Order {OrderId}: delivery collection {FulfillmentId} recorded ({Method}).", collected.OrderId, collected.FulfillmentId, method);
    }
}

/// <summary>
/// delivery.CourierRemittanceRecorded -> mouvement d'espèces sur la caisse ouverte de l'établissement : entrée de ce que le
/// livreur a remis, ou sortie de ce qu'on lui a payé. Motif « Remise livreur R-xxxxxxxx » : un message rejoué ne bouge
/// pas deux fois la caisse. Sans caisse ouverte, rien n'est fait (journalisé) : la remise reste, à reporter à la main.
/// Journal : ids et montants seulement, jamais le nom du livreur.
/// </summary>
internal sealed class CourierRemittanceFlow(IServiceScopeFactory scopes, ILogger<CourierRemittanceFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("delivery", "CourierRemittanceRecorded"))
        {
            return;
        }

        var remittance = message.Read<SaasRestaurant.Contracts.Delivery.CourierRemittanceRecorded>();
        if (remittance.Counted.Amount == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(remittance.TenantId));

        var session = await services.GetRequiredService<IQueryHandler<GetOpenCashSessionQuery, CashSessionDto?>>()
            .HandleAsync(new GetOpenCashSessionQuery(remittance.LocationId), cancellationToken);
        if (session is null)
        {
            logger.LogWarning("Courier remittance {RemittanceId}: no open cash session at location {LocationId}, cash movement not recorded.",
                remittance.RemittanceId, remittance.LocationId);
            return;
        }

        var reason = Reason(remittance.RemittanceId);
        if (session.Movements.Any(movement => movement.Reason == reason))
        {
            return;
        }

        var kind = remittance.Counted.Amount > 0 ? CashMovementKind.PayIn : CashMovementKind.PayOut;
        await services.GetRequiredService<ICommandHandler<RecordCashMovementCommand>>().HandleAsync(
            new RecordCashMovementCommand(session.Id, kind, Math.Abs(remittance.Counted.Amount), reason), cancellationToken);
        logger.LogInformation("Courier remittance {RemittanceId}: cash movement {Kind} recorded on session {CashSessionId}.",
            remittance.RemittanceId, kind, session.Id);
    }

    public static string Reason(Guid remittanceId) => "Remise livreur R-" + remittanceId.ToString("N")[^8..].ToUpperInvariant();
}

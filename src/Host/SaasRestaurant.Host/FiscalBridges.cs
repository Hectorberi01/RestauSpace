using Fiscal.Application;
using Orders.Application.Queries;
using Payments.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using OrdersContracts = SaasRestaurant.Contracts.Orders;
using PaymentsContracts = SaasRestaurant.Contracts.Payments;

namespace SaasRestaurant.Host;

// Les ports déclarés par Fiscal, servis par Tenants, Orders et Payments, et le flux qui demande les factures normalisées.
// Fail-closed : un tenant autre que celui du contexte n'a rien.

/// <summary>Fiscal -> Tenants : écritures métier acceptées (inconnu = non) et devise.</summary>
internal sealed class FiscalTenantsFromTenants(CachedTenants tenants) : IFiscalTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<string?> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await tenants.FindAsync(tenantId, cancellationToken))?.DefaultCurrencyCode;
}

/// <summary>Fiscal -> Orders, Payments : lignes figées de la commande et ce que chaque moyen a réglé.</summary>
internal sealed class FiscalOrdersFromOrders(
    ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders, IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> payments) : IFiscalOrders
{
    public async Task<FiscalOrder?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        OrderDto order;
        try
        {
            order = await orders.HandleAsync(new GetOrderByIdQuery(orderId), cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var paid = await payments.HandleAsync(new GetOrderPaymentsQuery(orderId), cancellationToken);
        return new FiscalOrder(
            order.Id,
            order.LocationId,
            order.Number,
            order.CurrencyCode,
            order.Total,
            order.Items
                .Select(item => new FiscalOrderLine(
                    item.ProductName + (item.VariantName is { Length: > 0 } variant ? $" ({variant})" : string.Empty), item.Quantity, item.TotalWithTax, item.TaxRate))
                .ToList(),
            order.DeliveryFee,
            paid.Payments.Where(payment => payment.SettledAmount > 0).Select(payment => new FiscalOrderPayment(payment.Method.ToString(), payment.SettledAmount)).ToList());
    }
}

/// <summary>
/// Orders, Payments -> Fiscal : une commande terminée demande sa facture de vente, un remboursement réussi sa facture
/// d'avoir. Sans profil fiscal actif, rien n'est demandé. Les envois à la DGI se font ensuite par la file du module.
/// </summary>
internal sealed class FiscalFlow(IServiceScopeFactory scopes, ILogger<FiscalFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (("orders", "OrderCompleted") or ("payments", "RefundSucceeded")))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        try
        {
            if (message.Type == "OrderCompleted")
            {
                var completed = message.Read<OrdersContracts.OrderCompleted>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(completed.TenantId));
                await services.GetRequiredService<ICommandHandler<IssueSaleInvoiceCommand, Guid?>>()
                    .HandleAsync(new IssueSaleInvoiceCommand(completed.OrderId), cancellationToken);
                return;
            }

            var refund = message.Read<PaymentsContracts.RefundSucceeded>();
            services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(refund.TenantId));
            var payment = await services.GetRequiredService<IQueryHandler<GetPaymentByIdQuery, PaymentDto>>()
                .HandleAsync(new GetPaymentByIdQuery(refund.PaymentId), cancellationToken);
            if (payment.Purpose == Payments.Domain.PaymentPurpose.ReservationDeposit && payment.ReservationId == payment.OrderId)
            {
                // Acompte rendu avant d'être déduit d'une addition : aucune facture de vente à corriger.
                return;
            }

            await services.GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid?>>()
                .HandleAsync(new IssueCreditNoteCommand(refund.OrderId, refund.RefundId, refund.Amount.Amount, payment.Method.ToString()), cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException)
        {
            // Une facture impossible à constituer (données incohérentes) ne doit pas bloquer la file de l'outbox.
            logger.LogWarning("Fiscal invoice not requested for {MessageType}: {Code}.", message.Type, (exception as DomainException)?.Error.Code ?? exception.GetType().Name);
        }
    }
}

using Orders.Application.Queries;
using Payments.Application.Queries;
using Printing.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Application.Abstractions;

namespace SaasRestaurant.Host;

/// <summary>
/// Storefront -> Orders, Payments, Fiscal, Tenants, Locations : le reçu d'une commande du tenant courant, mis en forme
/// comme le reçu imprimé (<see cref="PrintingReceiptsFromOrders"/>). Brouillon ou commande inconnue : null. Aucune donnée
/// personnelle du client, seulement l'id de sa fiche.
/// </summary>
internal sealed class StorefrontReceiptsFromOrders(
    ITenantContext tenantContext,
    IPrintingReceipts printed,
    IQueryHandler<GetOrderByIdQuery, OrderDto> orders,
    IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> payments) : IStorefrontReceipts
{
    public async Task<StorefrontReceipt?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
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

        if (order.Status == Orders.Domain.OrderStatus.Draft || await printed.FindAsync(tenantId, orderId.Value, cancellationToken) is not { } receipt)
        {
            return null;
        }

        var due = (await payments.HandleAsync(new GetOrderPaymentsQuery(orderId.Value), cancellationToken)).AmountDue;
        return new StorefrontReceipt(
            orderId,
            LocationId.From(order.LocationId),
            order.CustomerId is { } customer ? CustomerId.From(customer) : null,
            receipt.RestaurantName,
            receipt.LocationName,
            receipt.Address,
            receipt.OrderNumber,
            receipt.ServiceMode,
            receipt.Date,
            order.Status == Orders.Domain.OrderStatus.Cancelled,
            receipt.Lines.Select(line => new ReceiptItem(line.Quantity, line.Name, line.Details, line.Amount)).ToList(),
            receipt.Totals.Select(total => new ReceiptAmount(total.Label, total.Amount)).ToList(),
            receipt.Total,
            receipt.Payments.Select(payment => new ReceiptAmount(payment.Label, payment.Amount)).ToList(),
            due > 0 && order.Status != Orders.Domain.OrderStatus.Cancelled ? TicketText.Money(due, order.CurrencyCode) : null,
            receipt.Footer,
            receipt.QrCode,
            order.PriceTaxMode == Orders.Domain.PriceTaxMode.TaxInclusive);
    }
}

/// <summary>
/// Storefront -> Notifications : le lien du reçu part par le compte WhatsApp (ou SMS) du restaurant, type
/// <c>order.receipt</c>. Rien d'envoyé (pas de fournisseur, pas de modèle validé, client injoignable...) : la raison est
/// rendue telle quelle. Fail-closed hors du tenant du contexte ; ni le numéro ni le nom ne sont journalisés.
/// </summary>
internal sealed class StorefrontReceiptMessagesFromNotifications(
    ITenantContext tenantContext,
    ICommandHandler<Notifications.Application.Commands.SendNotificationCommand, Notifications.Application.Commands.SendNotificationResult> send)
    : IStorefrontReceiptMessages
{
    public async Task<ReceiptMessageOutcome> SendAsync(TenantId tenantId, ReceiptMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("Receipts can only be sent for the tenant of the current context.");
        }

        var channel = message.Channel == ReceiptChannel.Sms ? Notifications.Domain.NotificationChannel.Sms : Notifications.Domain.NotificationChannel.WhatsApp;
        var result = await send.HandleAsync(
            new Notifications.Application.Commands.SendNotificationCommand(
                Notifications.Domain.NotificationTypes.OrderReceipt.Key.Value,
                channel,
                message.CustomerId?.Value,
                null,
                message.Phone?.Value,
                message.LocationId.Value,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["restaurant.name"] = message.RestaurantName,
                    ["location.name"] = message.LocationName,
                    ["order.number"] = message.OrderNumber,
                    ["order.total"] = message.Total,
                    ["receipt.url"] = message.Url,
                },
                null,
                message.IdempotencyKey),
            cancellationToken);
        return result.NotificationIds.Count > 0
            ? new ReceiptMessageOutcome(true, null)
            : new ReceiptMessageOutcome(false, result.Skipped?.ToString());
    }
}

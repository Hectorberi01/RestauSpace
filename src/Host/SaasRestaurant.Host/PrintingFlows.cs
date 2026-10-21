using System.Globalization;
using Kitchen.Application.Queries;
using Kitchen.Domain;
using Locations.Application.Queries;
using Orders.Application.Queries;
using Orders.Domain;
using Payments.Application.Queries;
using Payments.Domain;
using Printing.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using KitchenContracts = SaasRestaurant.Contracts.Kitchen;

namespace SaasRestaurant.Host;

/// <summary>Mise en forme commune aux tickets : montants et dates pour l'impression, dans la langue de la console.</summary>
internal static class TicketText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Money(decimal amount, string currency)
    {
        var digits = currency is "XOF" or "XAF" or "GNF" or "JPY" ? 0 : 2;
        var symbol = currency switch
        {
            "XOF" or "XAF" => "F CFA",
            "EUR" => "€",
            "USD" => "$",
            _ => currency,
        };
        // Espace normale entre les milliers : les imprimantes ne connaissent pas l'espace fine insécable.
        return amount.ToString("N" + digits, French).Replace(' ', ' ').Replace(' ', ' ') + " " + symbol;
    }

    public static string Date(DateTimeOffset utc, string? timeZoneId)
    {
        var local = utc;
        try
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                local = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            }
        }
        catch (TimeZoneNotFoundException)
        {
        }

        return local.ToString("dd/MM/yyyy HH:mm", French);
    }

    public static string ServiceMode(OrderType type) => type switch
    {
        OrderType.DineIn => "Sur place",
        OrderType.Takeaway => "À emporter",
        _ => "Livraison",
    };

    /// <summary>
    /// Détail d'une ligne de reçu : ses options, puis pour une formule chaque produit choisi (« Plat : Poulet braisé +
    /// Alloco (+500 F CFA) »), séparés par « ; ». Null s'il n'y a rien.
    /// </summary>
    public static string? ItemDetails(OrderItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var parts = new List<string>();
        if (item.Modifiers.Count > 0)
        {
            parts.Add("+ " + string.Join(", ", item.Modifiers.Select(modifier => modifier.Name)));
        }

        foreach (var component in item.Components ?? [])
        {
            var text = $"{component.StepName} : {component.ProductName}"
                + (component.VariantName is { Length: > 0 } variant ? $" ({variant})" : string.Empty)
                + (component.Modifiers.Count > 0 ? " + " + string.Join(", ", component.Modifiers.Select(modifier => modifier.Name)) : string.Empty);
            parts.Add(text);
        }

        return parts.Count == 0 ? null : string.Join(" ; ", parts);
    }

    public static string Method(PaymentMethodType method) => method switch
    {
        PaymentMethodType.Cash => "Espèces",
        PaymentMethodType.CashOnDelivery => "Espèces (livreur)",
        PaymentMethodType.CardOnSite => "Carte (terminal)",
        PaymentMethodType.MobileMoneyOnSite or PaymentMethodType.MobileMoney => "Mobile money",
        PaymentMethodType.MealVoucher => "Titre-restaurant",
        PaymentMethodType.BankTransfer => "Virement",
        PaymentMethodType.GiftCard => "Carte cadeau / avoir",
        PaymentMethodType.DeliveryPlatform => "Plateforme de livraison",
        PaymentMethodType.PayPal => "PayPal",
        _ => "Carte",
    };
}

/// <summary>
/// Kitchen -> Printing : à la création d'un ticket de cuisine, son bon part sur les imprimantes de cuisine de
/// l'établissement. Une imprimante absente ou une erreur d'impression ne bloque jamais la commande : on journalise et on
/// s'arrête. Aucune donnée du client sur le bon.
/// </summary>
internal sealed class KitchenPrintFlow(IServiceScopeFactory scopes, ILogger<KitchenPrintFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("kitchen", "KitchenTicketCreated"))
        {
            return;
        }

        var created = message.Read<KitchenContracts.KitchenTicketCreated>();
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(created.TenantId));

        try
        {
            var ticket = await services.GetRequiredService<IQueryHandler<GetTicketByIdQuery, KitchenTicketDto>>()
                .HandleAsync(new GetTicketByIdQuery(created.TicketId), cancellationToken);
            var order = await services.GetRequiredService<IQueryHandler<GetOrderByIdQuery, OrderDto>>()
                .HandleAsync(new GetOrderByIdQuery(created.OrderId), cancellationToken);
            var stations = await services.GetRequiredService<IQueryHandler<ListStationsQuery, IReadOnlyList<StationDto>>>()
                .HandleAsync(new ListStationsQuery(created.LocationId), cancellationToken);
            var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
                .HandleAsync(new GetLocationByIdQuery(created.LocationId), cancellationToken);

            var input = new KitchenTicketInput(
                order.Number,
                TicketText.ServiceMode(order.Type),
                ticket.Priority != KitchenPriority.Normal,
                ticket.Items
                    .Select(item => new KitchenTicketItemInput(
                        item.StationId,
                        item.Quantity,
                        item.Name,
                        (item.VariantName is { } variant ? [variant] : Array.Empty<string>()).Concat(item.Modifiers).ToList(),
                        item.Note))
                    .ToList(),
                stations.ToDictionary(station => station.Id, station => station.Name),
                TicketText.Date(ticket.CreatedAtUtc, location.TimeZoneId));
            await services.GetRequiredService<ICommandHandler<PrintKitchenTicketCommand, int>>()
                .HandleAsync(new PrintKitchenTicketCommand(created.LocationId, $"kitchen-{created.TicketId:N}", input), cancellationToken);
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException or ConcurrencyException)
        {
            logger.LogWarning("Kitchen ticket {TicketId} not printed: {ErrorType}.", created.TicketId, exception.GetType().Name);
        }
    }
}

/// <summary>
/// Printing -> Orders, Payments, Tenants, Locations : le reçu d'une commande du tenant courant, déjà mis en forme. Aucune
/// donnée personnelle du client ; les montants sont ceux figés par la commande.
/// </summary>
internal sealed class PrintingReceiptsFromOrders(
    ITenantContext tenantContext,
    CachedTenants tenants,
    IQueryHandler<GetOrderByIdQuery, OrderDto> orders,
    IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto> payments,
    IQueryHandler<GetLocationByIdQuery, LocationDto> locations,
    IQueryHandler<Fiscal.Application.GetOrderFiscalQuery, Fiscal.Application.OrderFiscalDto?> fiscal) : IPrintingReceipts
{
    public async Task<OrderReceiptInput?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId || await tenants.FindAsync(tenantId, cancellationToken) is not { } tenant)
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

        var location = await locations.HandleAsync(new GetLocationByIdQuery(order.LocationId), cancellationToken);
        var paid = await payments.HandleAsync(new GetOrderPaymentsQuery(orderId), cancellationToken);
        var fiscalInfo = await fiscal.HandleAsync(new Fiscal.Application.GetOrderFiscalQuery(orderId), cancellationToken);
        var sale = fiscalInfo?.Sale;
        var footer = FiscalFooter(fiscalInfo);
        string Amount(decimal value) => TicketText.Money(value, order.CurrencyCode);

        var totals = new List<ReceiptLine> { new("Sous-total", Amount(order.SubTotal)) };
        totals.AddRange(order.Discounts.Select(discount => new ReceiptLine("Remise" + (discount.Label is { Length: > 0 } label ? " " + label : string.Empty), "-" + Amount(discount.Amount))));
        if (order.DeliveryFee is { } fee and > 0)
        {
            totals.Add(new ReceiptLine("Frais de livraison", Amount(fee)));
        }

        totals.Add(new ReceiptLine(order.PriceTaxMode == Orders.Domain.PriceTaxMode.TaxInclusive ? "dont taxes" : "Taxes", Amount(order.TaxTotal)));

        var captured = paid.Payments.Where(payment => payment.CapturedAmount > 0).ToList();
        var paymentLines = captured.Select(payment => new ReceiptLine(TicketText.Method(payment.Method), Amount(payment.CapturedAmount))).ToList();
        var refunded = captured.Sum(payment => payment.RefundedAmount);
        if (refunded > 0)
        {
            paymentLines.Add(new ReceiptLine("Remboursé", "-" + Amount(refunded)));
        }

        return new OrderReceiptInput(
            order.LocationId,
            string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName,
            location.Name,
            $"{location.Address.Street}, {location.Address.City}",
            order.Number,
            TicketText.ServiceMode(order.Type),
            TicketText.Date(order.CompletedAtUtc ?? order.PlacedAtUtc ?? order.CreatedAtUtc, location.TimeZoneId),
            order.Items
                .Select(item => new CustomerTicketLine(
                    item.Quantity,
                    item.ProductName + (item.VariantName is { Length: > 0 } variant ? $" ({variant})" : string.Empty),
                    TicketText.ItemDetails(item),
                    Amount(item.LineTotal)))
                .ToList(),
            totals,
            Amount(order.Total),
            paymentLines,
            footer,
            sale?.Status == Fiscal.Domain.FiscalInvoiceStatus.Certified ? sale.QrCode : null);
    }

    /// <summary>Mentions fiscales du reçu : IFU, RCCM et, une fois la facture normalisée certifiée, ses éléments de sécurité.</summary>
    private static List<string> FiscalFooter(Fiscal.Application.OrderFiscalDto? info)
    {
        var footer = new List<string>();
        if (info is null)
        {
            return footer;
        }

        footer.Add($"IFU : {info.Ifu}");
        if (info.Rccm is { Length: > 0 } rccm)
        {
            footer.Add($"RCCM : {rccm}");
        }

        if (info.Sale is { Status: Fiscal.Domain.FiscalInvoiceStatus.Certified } sale)
        {
            footer.Add("FACTURE NORMALISÉE");
            footer.Add($"Code MECeF/DGI : {sale.CodeMecef}");
            footer.Add($"MECeF NIM : {sale.Nim}");
            footer.Add($"MECeF Compteurs : {sale.Counters}");
            footer.Add($"MECeF Heure : {sale.DgiDateTime}");
        }
        else if (info.Sale is not null)
        {
            footer.Add("Facture normalisée en cours de certification");
        }

        return footer;
    }
}

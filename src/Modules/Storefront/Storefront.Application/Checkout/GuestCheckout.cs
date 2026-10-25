using Storefront.Application.Abstractions;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application.Checkout;

/// <summary>Le client au checkout : nom et téléphone ou e-mail selon la règle du restaurant, consentement marketing facultatif.</summary>
public sealed record GuestContactInput(string? Name, string? Email = null, string? Phone = null, bool MarketingConsent = false, string? Language = null)
{
    public override string ToString() => "GuestContactInput (***)";
}

/// <summary>La livraison au checkout : zone choisie, destinataire et adresse (repère et consignes facultatifs).</summary>
public sealed record DeliveryAddressInput(Guid ZoneId, string? AddressLine, string? Landmark = null, string? Instructions = null)
{
    public override string ToString() => $"DeliveryAddressInput({ZoneId}, ***)";
}

/// <param name="MinimumOrderAmount">Minimum à atteindre (établissement, et zone de livraison) ; null = aucun. Comparé au total hors frais.</param>
/// <param name="DeliveryFee">Frais de la zone, compris dans <paramref name="Total"/> ; null hors livraison ou sans zone choisie.</param>
/// <param name="Coupon">Le coupon envoyé : accepté avec sa remise (dans <paramref name="DiscountTotal"/>), ou refusé (même motif pour tout refus).</param>
/// <param name="CanPayAtCounter">Le paiement au comptoir est proposé pour ce canal et ce montant.</param>
/// <param name="CanPayOnline">Le paiement en ligne est proposé pour ce canal et un prestataire est configuré.</param>
/// <param name="OnlinePaymentOptions">Les prestataires en ligne proposés, pour le choix du client.</param>
public sealed record CartQuoteDto(
    Guid LocationId,
    ServiceMode Mode,
    string CurrencyCode,
    bool PricesIncludeTax,
    IReadOnlyList<CartQuoteLine> Lines,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal? MinimumOrderAmount,
    bool MeetsMinimum,
    bool CanPayAtCounter,
    bool CanPayOnline,
    IReadOnlyList<OnlinePaymentOption> OnlinePaymentOptions,
    decimal? DeliveryFee = null,
    Guid? DeliveryZoneId = null,
    CartCouponDto? Coupon = null,
    CartRewardDto? Reward = null);

/// <param name="TrackingToken">À garder par l'application : il ouvre le suivi et l'annulation de cette commande.</param>
/// <param name="AcceptBeforeUtc">Validation manuelle : sans acceptation avant cette heure, la commande est refusée.</param>
/// <param name="PayBeforeUtc">Paiement en ligne : sans paiement avant cette heure, la commande est annulée.</param>
/// <param name="TicketPrinted">Borne : le ticket client est parti vers l'imprimante de l'établissement.</param>
public sealed record GuestOrderPlacedDto(
    Guid OrderId,
    string Number,
    GuestOrderStatus Status,
    string CurrencyCode,
    decimal Total,
    PaymentMode Payment,
    DateTimeOffset? AcceptBeforeUtc,
    DateTimeOffset? PayBeforeUtc,
    string TrackingToken,
    bool TicketPrinted = false);

/// <summary>Où en est le paiement, vu par le client.</summary>
public enum GuestPaymentState
{
    /// <summary>À payer au comptoir, à la remise.</summary>
    AtCounter = 1,
    AwaitingPayment = 2,

    /// <summary>Le prestataire n'a pas encore répondu.</summary>
    Processing = 3,
    Paid = 4,
    Refunded = 5,
}

/// <param name="CheckoutUrl">Page du prestataire où envoyer le client ; null quand la demande est poussée sur son téléphone.</param>
public sealed record GuestPaymentStartedDto(Guid PaymentId, Uri? CheckoutUrl);

/// <param name="AmountDueAtCounter">Reste à payer au comptoir (0 si payée en ligne, annulée ou terminée).</param>
/// <param name="EstimatedReadyAtUtc">Heure demandée, ou heure de validation + délai de préparation.</param>
/// <param name="CanCancel">Le client peut encore annuler : la commande n'a pas été acceptée.</param>
/// <param name="CanPay">Paiement en ligne attendu et encore possible ; les prestataires sont dans <c>PaymentOptions</c>.</param>
/// <param name="Delivery">Livraison : zone, frais, étape et heure promise (ni adresse ni livreur).</param>
public sealed record GuestOrderDto(
    Guid OrderId,
    string Number,
    Guid LocationId,
    string LocationName,
    GuestOrderStatus Status,
    PaymentMode Payment,
    string CurrencyCode,
    IReadOnlyList<CartQuoteLine> Lines,
    decimal Total,
    decimal AmountDueAtCounter,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? EstimatedReadyAtUtc,
    DateTimeOffset? AcceptBeforeUtc,
    bool CanCancel,
    GuestPaymentState PaymentState,
    decimal AmountPaid,
    DateTimeOffset? PayBeforeUtc,
    bool CanPay,
    IReadOnlyList<OnlinePaymentOption> PaymentOptions,
    ServiceMode Mode = ServiceMode.Takeaway,
    decimal DiscountTotal = 0m,
    GuestDeliveryDto? Delivery = null);

/// <summary>La livraison vue par le client. <paramref name="Status"/> : Pending, Assigned, OutForDelivery, Completed, Failed, Cancelled.</summary>
public sealed record GuestDeliveryDto(string ZoneName, decimal Fee, string Status, DateTimeOffset PromisedAtUtc);

public static class CheckoutErrors
{
    public const int MaxLines = 50;
    public const int MaxQuantity = 99;

    public static readonly DomainError Unavailable =
        new("storefront.checkout.unavailable", "The restaurant does not take orders through this application at the moment.");
    public static readonly DomainError Paused = new("storefront.checkout.paused", "Online ordering is paused at this location.");
    public static readonly DomainError Closed = new("storefront.checkout.closed", "The location is closed: schedule the order for later.");
    public static readonly DomainError ClosedAtSchedule = new("storefront.checkout.closed_at_schedule", "The location is closed at the requested time.");
    public static readonly DomainError ScheduleTooSoon =
        new("storefront.checkout.schedule_too_soon", "The requested time is earlier than the preparation time allows.");
    public static readonly DomainError ServiceModeUnavailable =
        new("storefront.checkout.service_mode_unavailable", "This location does not offer this service mode online.");
    public static readonly DomainError PaymentModeNotAllowed =
        new("storefront.checkout.payment_mode_not_allowed", "This payment mode is not offered for this channel.");
    public static readonly DomainError OnlinePaymentUnavailable =
        new("storefront.checkout.online_payment_unavailable", "No online payment provider is set up at this location: pay at the counter.");
    public static readonly DomainError PaymentNotExpected =
        new("storefront.payment.not_expected", "This order is not waiting for an online payment.", DomainErrorKind.Conflict);
    public static readonly DomainError ReturnUrlNotAllowed =
        new("storefront.payment.return_url_not_allowed", "The return page is not declared for this application.");
    public static readonly DomainError CartEmpty = new("storefront.checkout.cart_empty", "The cart is empty.");
    public static readonly DomainError CartTooLarge = new("storefront.checkout.cart_too_large", "A cart holds 50 lines of 99 items at most.");
    public static readonly DomainError BelowMinimum = new("storefront.checkout.below_minimum", "The order is below the minimum amount of the location.");
    public static readonly DomainError CounterLimitExceeded =
        new("storefront.checkout.counter_limit_exceeded", "This amount cannot be paid at the counter: pay online.");
    public static readonly DomainError PriceChanged =
        new("storefront.checkout.price_changed", "Prices have changed since the cart was quoted: review the cart.", DomainErrorKind.Conflict);
    public static readonly DomainError TooManyPendingPayments = new(
        "storefront.checkout.too_many_pending_payments",
        "Too many orders are waiting for an online payment: pay or cancel them first.",
        DomainErrorKind.Conflict);
    public static readonly DomainError CustomerRequired =
        new("storefront.checkout.customer_required", "A name and a phone number or an e-mail address are required.");

    // Aussi la réponse pour un jeton faux ou d'un autre restaurant : on ne révèle jamais qu'une commande existe.
    public static readonly DomainError OrderNotFound = new("storefront.order.not_found", "The order does not exist.");
}

/// <summary>Ce qui est commun au devis et à la validation : l'application, l'établissement, ses réglages et la règle du canal.</summary>
internal sealed record CheckoutContext(
    SalesApp App, StorefrontRestaurant Restaurant, bool Enabled, StorefrontLocation Location, OnlineOrderingSettings Settings, ChannelPolicy Policy);

internal static class CheckoutReader
{
    public static async Task<CheckoutContext> ContextAsync(
        StorefrontWorkspace workspace, IStorefrontClient client, Guid locationId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var (app, restaurant, enabled) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = (await workspace.Locations.ListAsync(tenant, nowUtc, cancellationToken))
            .SingleOrDefault(item => item.LocationId.Value == locationId && StorefrontReader.Serves(app, client, item.LocationId))
            ?? throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        var settings = await workspace.Settings.FindAsync(tenant, location.LocationId, cancellationToken)
            ?? OnlineOrderingSettings.Default(new LocationReference(tenant, location.LocationId), new FixedClock(nowUtc));
        return new CheckoutContext(app, restaurant, enabled, location, settings, settings.PolicyFor(app.Kind));
    }

    public static void EnsureCartShape(IReadOnlyList<CartLine>? lines)
    {
        DomainException.ThrowIf(lines is not { Count: > 0 }, CheckoutErrors.CartEmpty);
        DomainException.ThrowIf(lines!.Count > CheckoutErrors.MaxLines || lines.Any(line => line.Quantity is < 1 or > CheckoutErrors.MaxQuantity), CheckoutErrors.CartTooLarge);
    }

    public static bool CounterAllows(ChannelPolicy policy, decimal total) => policy.PayAtCounter && (policy.CounterLimit is not { } limit || total <= limit);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}

/// <summary>
/// Devis d'un panier pour l'établissement et le canal de l'application : lignes, totaux et moyens de paiement
/// proposés. Les prix viennent du serveur (Orders et Catalog), exactement comme à la validation.
/// </summary>
/// <param name="CouponCode">Coupon saisi par le client : le devis dit s'il est accepté et ce qu'il retire.</param>
/// <param name="DeliveryZoneId">Livraison : la zone choisie ; ses frais et son minimum entrent dans le devis.</param>
/// <param name="RewardId">Client connecté : la récompense de fidélité à utiliser (ses points sont débités à la validation).</param>
public sealed record QuoteCartQuery(
    Guid LocationId, ServiceMode Mode, IReadOnlyList<CartLine>? Lines, string? CouponCode = null, Guid? DeliveryZoneId = null, Guid? RewardId = null)
    : IQuery<CartQuoteDto>;

public sealed class QuoteCartHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IStorefrontCoupons coupons,
    IStorefrontDelivery delivery,
    IStorefrontLoyalty loyalty,
    IClock clock)
    : IQueryHandler<QuoteCartQuery, CartQuoteDto>
{
    public async Task<CartQuoteDto> HandleAsync(QuoteCartQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = await CheckoutReader.ContextAsync(workspace, client, query.LocationId, clock.UtcNow, cancellationToken);
        CheckoutReader.EnsureCartShape(query.Lines);
        DomainException.ThrowIf(!Enum.IsDefined(query.Mode), CheckoutErrors.ServiceModeUnavailable);

        var price = await CheckoutPricing.PriceAsync(
            workspace.CurrentTenant, context, query.Mode, query.Lines!, query.CouponCode, query.DeliveryZoneId, zoneRequired: false, orders, coupons, delivery,
            cancellationToken, query.RewardId, client.CustomerId, loyalty);
        var quote = price.Quote;
        var options = context.Policy.PayOnline
            ? await payments.OptionsAsync(workspace.CurrentTenant, context.Location.LocationId, quote.CurrencyCode, quote.Total, cancellationToken)
            : [];
        return new CartQuoteDto(
            query.LocationId, query.Mode, quote.CurrencyCode, quote.PricesIncludeTax, quote.Lines, quote.SubTotal, quote.DiscountTotal, quote.TaxTotal,
            quote.Total, price.Minimum, price.MeetsMinimum, CheckoutReader.CounterAllows(context.Policy, quote.Total), options.Count > 0, options,
            quote.DeliveryFee, price.Zone?.ZoneId, CheckoutPricing.ToDto(price.Coupon), CheckoutPricing.ToDto(price.Reward));
    }
}

/// <summary>
/// Validation d'une commande invité. Le restaurant vient de la clé, le canal de l'application ; les prix sont recalculés
/// par le serveur et font foi (<paramref name="ExpectedTotal"/>, s'il est donné, doit être égal). Pour un site ou une
/// application, la fiche client est retrouvée ou créée (nom, téléphone ou e-mail) ; une borne ne demande rien. Acceptation
/// automatique : la commande part en cuisine ; manuelle : le personnel a jusqu'à l'échéance pour l'accepter. Le coupon
/// est compté et la fiche de livraison créée, avec ses frais, avant la validation : le total payé est celui du devis.
/// En livraison, <paramref name="ScheduledForUtc"/> est l'heure de livraison demandée.
/// Paiement en ligne (hors borne) : au plus <see cref="CheckoutLimits.MaxPendingPayments"/> commandes impayées par
/// application, adresse IP et client ; le délai pour payer est plafonné à <see cref="CheckoutLimits.PaymentHoldMinutes"/>.
/// </summary>
public sealed record PlaceGuestOrderCommand(
    Guid LocationId,
    ServiceMode Mode,
    IReadOnlyList<CartLine>? Lines,
    PaymentMode Payment,
    GuestContactInput? Customer = null,
    DateTimeOffset? ScheduledForUtc = null,
    decimal? ExpectedTotal = null,
    string? CouponCode = null,
    DeliveryAddressInput? Delivery = null,
    Guid? RewardId = null,
    string? TableToken = null) : ICommand<GuestOrderPlacedDto>
{
    public override string ToString() => $"PlaceGuestOrderCommand({LocationId}, {Mode}, {Payment}, ***)";
}

public sealed class PlaceGuestOrderHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontOrders orders,
    IStorefrontCustomers customers,
    IStorefrontPayments payments,
    IStorefrontTickets tickets,
    IStorefrontCoupons coupons,
    IStorefrontDelivery delivery,
    IStorefrontLoyalty loyalty,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens,
    IStorefrontUnitOfWork unitOfWork,
    IStorefrontTables tables,
    IClock clock,
    CheckoutLimits limits) : ICommandHandler<PlaceGuestOrderCommand, GuestOrderPlacedDto>
{
    public async Task<GuestOrderPlacedDto> HandleAsync(PlaceGuestOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
        var context = await CheckoutReader.ContextAsync(workspace, client, command.LocationId, now, cancellationToken);
        var (app, settings, policy) = (context.App, context.Settings, context.Policy);
        var tenant = workspace.CurrentTenant;

        DomainException.ThrowIf(!context.Enabled, CheckoutErrors.Unavailable);
        DomainException.ThrowIf(settings.IsPaused, CheckoutErrors.Paused);
        DomainException.ThrowIf(!Enum.IsDefined(command.Mode) || !settings.Offers(command.Mode), CheckoutErrors.ServiceModeUnavailable);
        DomainException.ThrowIf(!Enum.IsDefined(command.Payment), CheckoutErrors.PaymentModeNotAllowed);
        DomainException.ThrowIf(command.Payment == PaymentMode.Online && !policy.PayOnline, CheckoutErrors.PaymentModeNotAllowed);
        DomainException.ThrowIf(command.Payment == PaymentMode.Counter && !policy.PayAtCounter, CheckoutErrors.PaymentModeNotAllowed);
        CheckoutReader.EnsureCartShape(command.Lines);
        var table = await TableCheckout.ResolveAsync(tables, tenant, context.Location.LocationId, command, cancellationToken);
        var isDelivery = command.Mode == ServiceMode.Delivery;
        var signedIn = app.Kind == SalesAppKind.Kiosk ? null : await Customers.CustomerAccounts.CurrentAsync(workspace, client, customers, cancellationToken);

        var price = await CheckoutPricing.PriceAsync(
            tenant, context, command.Mode, command.Lines!, command.CouponCode, command.Delivery?.ZoneId, zoneRequired: isDelivery, orders, coupons, delivery,
            cancellationToken, command.RewardId, signedIn?.CustomerId, loyalty);
        DomainException.ThrowIf(price.Coupon is { IsValid: false }, DeliveryCheckoutErrors.CouponInvalid);

        // En livraison, l'heure demandée est celle de la livraison : la commande doit être prête un trajet plus tôt.
        var travel = TimeSpan.FromMinutes(price.Zone?.EstimatedMinutes ?? 0);
        var readyAt = command.ScheduledForUtc is { } scheduled ? scheduled - travel : now.AddMinutes(settings.PreparationMinutes);
        await EnsureOpenAsync(context, command.ScheduledForUtc is null ? null : readyAt, now, cancellationToken);
        if (settings.SlotCapacity is { } capacity)
        {
            var taken = await onlineOrders.CountInSlotAsync(tenant, context.Location.LocationId, OrderSlots.StartOf(readyAt), cancellationToken);
            DomainException.ThrowIf(taken >= capacity, DeliveryCheckoutErrors.SlotFull);
        }

        var quote = price.Quote;
        DomainException.ThrowIf(command.ExpectedTotal is { } expected && expected != quote.Total, CheckoutErrors.PriceChanged);
        DomainException.ThrowIf(!price.MeetsMinimum, CheckoutErrors.BelowMinimum);
        DomainException.ThrowIf(command.Payment == PaymentMode.Counter && !CheckoutReader.CounterAllows(policy, quote.Total), CheckoutErrors.CounterLimitExceeded);
        DomainException.ThrowIf(
            command.Payment == PaymentMode.Online
                && (await payments.OptionsAsync(tenant, context.Location.LocationId, quote.CurrencyCode, quote.Total, cancellationToken)).Count == 0,
            CheckoutErrors.OnlinePaymentUnavailable);

        Guid? customerId = null;
        DeliveryToPlace? deliveryToPlace = null;
        if (signedIn is not null)
        {
            // Client connecté : sa fiche fait foi ; un téléphone donné au checkout ne sert qu'à la livraison.
            customerId = signedIn.CustomerId.Value;
            if (isDelivery)
            {
                var phone = string.IsNullOrWhiteSpace(command.Customer?.Phone) ? signedIn.Phone : command.Customer.Phone;
                DomainException.ThrowIf(
                    string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(command.Delivery!.AddressLine), DeliveryCheckoutErrors.AddressRequired);
                deliveryToPlace = new DeliveryToPlace(
                    price.Zone!.ZoneId, signedIn.Name, phone!.Trim(), command.Delivery!.AddressLine!.Trim(), command.Delivery.Landmark,
                    command.Delivery.Instructions, command.ScheduledForUtc);
            }

            if (command.Customer?.MarketingConsent == true)
            {
                await customers.GrantMarketingConsentAsync(tenant, signedIn.CustomerId, cancellationToken);
            }
        }
        else if (app.Kind != SalesAppKind.Kiosk && (table is null || !string.IsNullOrWhiteSpace(command.Customer?.Name)))
        {
            // À table, les coordonnées sont facultatives : le client est servi là où il est assis.
            var contact = command.Customer;
            DomainException.ThrowIf(
                contact is null || string.IsNullOrWhiteSpace(contact.Name) || (string.IsNullOrWhiteSpace(contact.Email) && string.IsNullOrWhiteSpace(contact.Phone)),
                CheckoutErrors.CustomerRequired);
            if (isDelivery)
            {
                DomainException.ThrowIf(
                    string.IsNullOrWhiteSpace(contact!.Phone) || string.IsNullOrWhiteSpace(command.Delivery!.AddressLine), DeliveryCheckoutErrors.AddressRequired);
                deliveryToPlace = new DeliveryToPlace(
                    price.Zone!.ZoneId, contact.Name!.Trim(), contact.Phone!.Trim(), command.Delivery!.AddressLine!.Trim(), command.Delivery.Landmark,
                    command.Delivery.Instructions, command.ScheduledForUtc);
            }

            customerId = await customers.FindOrCreateAsync(
                tenant, new GuestCustomer(contact!.Name, contact.Email, contact.Phone, contact.MarketingConsent, contact.Language), cancellationToken);
        }

        var clientKey = app.Kind == SalesAppKind.Kiosk ? null : client.ClientKey;
        var holder = customerId is { } id ? CustomerId.From(id) : (CustomerId?)null;
        if (command.Payment == PaymentMode.Online && app.Kind != SalesAppKind.Kiosk)
        {
            var pending = await onlineOrders.CountAwaitingPaymentAsync(tenant, app.Id, clientKey, holder, now, cancellationToken);
            DomainException.ThrowIf(pending >= limits.MaxPendingPayments, CheckoutErrors.TooManyPendingPayments);
        }

        var session = table is null ? (TableSessionId?)null : await tables.SeatAsync(tenant, table.TableId, cancellationToken);
        var placed = await orders.PlaceAsync(
            tenant,
            new OrderToPlace(
                context.Location.LocationId, command.Mode, command.Lines!, customerId, app.Kind, app.Id, command.ScheduledForUtc,
                price.Coupon is { IsValid: true } ? price.Coupon.Code : null, deliveryToPlace, price.Reward?.RewardId, session),
            cancellationToken);
        var online = OnlineOrder.Record(
            tenant, placed.OrderId, app, context.Location.LocationId, command.Payment, policy.Acceptance,
            TimeSpan.FromMinutes(settings.ManualAcceptanceMinutes), limits.UnpaidDelay(settings.UnpaidExpiryMinutes), clock, readyAt, holder, clientKey);
        onlineOrders.Add(online);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Payée en ligne : acceptée (ou mise en attente d'acceptation) seulement une fois le paiement encaissé.
        var status = GuestOrderStatus.Received;
        if (online.State == OnlineOrderState.Handled)
        {
            await orders.AcceptAsync(tenant, placed.OrderId, cancellationToken);
            status = GuestOrderStatus.Accepted;
        }

        // Borne, paiement au comptoir : le ticket (numéro, total, « à payer au comptoir ») sort aussitôt.
        var printed = false;
        if (app.Kind == SalesAppKind.Kiosk && command.Payment == PaymentMode.Counter
            && await orders.FindAsync(tenant, placed.OrderId, cancellationToken) is { } view)
        {
            printed = await Kiosks.KioskTickets.PrintAsync(workspace, tickets, view, Kiosks.KioskTicketTexts.PayAtCounter, now, cancellationToken);
        }

        return new GuestOrderPlacedDto(
            placed.OrderId.Value, placed.Number, status, placed.CurrencyCode, placed.Total, command.Payment, online.AcceptBeforeUtc,
            online.PayBeforeUtc, tokens.Issue(tenant, placed.OrderId), printed);
    }

    private async Task EnsureOpenAsync(CheckoutContext context, DateTimeOffset? scheduledForUtc, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (scheduledForUtc is not { } at)
        {
            DomainException.ThrowIf(!context.Location.IsOperational || !context.Location.IsOpenNow, CheckoutErrors.Closed);
            return;
        }

        DomainException.ThrowIf(at < now.AddMinutes(context.Settings.PreparationMinutes), CheckoutErrors.ScheduleTooSoon);
        var then = (await workspace.Locations.ListAsync(workspace.CurrentTenant, at, cancellationToken))
            .SingleOrDefault(location => location.LocationId == context.Location.LocationId);
        DomainException.ThrowIf(then is not { IsOperational: true, IsOpenNow: true }, CheckoutErrors.ClosedAtSchedule);
    }
}

/// <summary>Suivi d'une commande par son jeton, avec la clé publique de son restaurant (404 sinon).</summary>
public sealed record GetGuestOrderQuery(string? Token) : IQuery<GuestOrderDto>
{
    public override string ToString() => "GetGuestOrderQuery (***)";
}

public sealed class GetGuestOrderHandler(
    StorefrontWorkspace workspace,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IStorefrontDelivery delivery,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens,
    IClock clock) : IQueryHandler<GetGuestOrderQuery, GuestOrderDto>
{
    public async Task<GuestOrderDto> HandleAsync(GetGuestOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (online, view) = await GuestOrders.LoadAsync(workspace, orders, onlineOrders, tokens, query.Token, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken))
            .SingleOrDefault(item => item.LocationId == view.LocationId);
        var settings = await workspace.Settings.FindAsync(tenant, view.LocationId, cancellationToken);
        var preparation = settings?.PreparationMinutes ?? OnlineOrderingSettings.DefaultPreparationMinutes;
        var placedAt = view.PlacedAtUtc ?? online.PlacedAtUtc;
        var open = view.Status is not (GuestOrderStatus.Cancelled or GuestOrderStatus.Completed);
        var summary = online.PaymentMode == PaymentMode.Online ? await payments.SummaryAsync(tenant, view.OrderId, cancellationToken) : null;
        var paymentState = summary switch
        {
            null => GuestPaymentState.AtCounter,
            { HasRefund: true } => GuestPaymentState.Refunded,
            { Due: <= 0 } => GuestPaymentState.Paid,
            _ when online.PaidAtUtc is not null => GuestPaymentState.Paid,
            { HasPending: true } => GuestPaymentState.Processing,
            _ => GuestPaymentState.AwaitingPayment,
        };
        var canPay = GuestOrders.CanPay(online, view, clock.UtcNow) && paymentState is GuestPaymentState.AwaitingPayment or GuestPaymentState.Processing;
        var options = canPay ? await payments.OptionsAsync(tenant, view.LocationId, view.CurrencyCode, summary?.Due ?? view.Total, cancellationToken) : [];
        var tracking = view.Mode == ServiceMode.Delivery ? await delivery.TrackAsync(tenant, view.OrderId, cancellationToken) : null;
        var estimated = tracking?.PromisedAtUtc ?? view.ScheduledForUtc ?? placedAt.AddMinutes(preparation);

        return new GuestOrderDto(
            view.OrderId.Value,
            view.Number,
            view.LocationId.Value,
            location?.Name ?? string.Empty,
            view.Status,
            online.PaymentMode,
            view.CurrencyCode,
            view.Lines,
            view.Total,
            open && online.PaymentMode == PaymentMode.Counter ? view.Total : 0m,
            placedAt,
            view.ScheduledForUtc,
            open ? estimated : null,
            online.IsAwaitingAcceptance ? online.AcceptBeforeUtc : null,
            GuestOrders.CanCancel(online, view),
            paymentState,
            summary?.Paid ?? 0m,
            online.IsAwaitingPayment ? online.PayBeforeUtc : null,
            canPay,
            options,
            view.Mode,
            view.DiscountTotal,
            tracking is null ? null : new GuestDeliveryDto(tracking.ZoneName, tracking.Fee, tracking.Status, tracking.PromisedAtUtc));
    }
}

/// <summary>Le client annule sa commande tant que le restaurant ne l'a pas acceptée.</summary>
public sealed record CancelGuestOrderCommand(string? Token) : ICommand
{
    public override string ToString() => "CancelGuestOrderCommand (***)";
}

public sealed class CancelGuestOrderHandler(
    StorefrontWorkspace workspace,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens,
    IStorefrontUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CancelGuestOrderCommand>
{
    public const string Reason = "Annulée par le client";

    public async Task HandleAsync(CancelGuestOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (online, view) = await GuestOrders.LoadAsync(workspace, orders, onlineOrders, tokens, command.Token, cancellationToken);
        DomainException.ThrowIf(!GuestOrders.CanCancel(online, view), OnlineOrderErrors.NotCancellable);

        var tenant = workspace.CurrentTenant;
        await GuestOrders.CloseAsync(tenant, online, orders, payments, Reason, cancellationToken);
        online.CancelByCustomer(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Pour le job : une commande dont l'échéance est passée. Impayée : elle est annulée et ses paiements en attente aussi.
/// Toujours pas acceptée : elle est refusée et, payée en ligne, remboursée. Le client est prévenu par l'annulation.
/// Déjà prise en charge ou close : plus rien à surveiller. Le tenant est celui du job (contexte), jamais une valeur reçue.
/// </summary>
public sealed record ResolveOverdueOnlineOrderCommand(Guid OnlineOrderId) : ICommand;

public sealed class ResolveOverdueOnlineOrderHandler(
    StorefrontWorkspace workspace,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IOnlineOrderRepository onlineOrders,
    IStorefrontUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ResolveOverdueOnlineOrderCommand>
{
    public const string Reason = "Non acceptée à temps";
    public const string UnpaidReason = "Non payée à temps";

    public async Task HandleAsync(ResolveOverdueOnlineOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var online = command.OnlineOrderId == Guid.Empty
            ? null
            : await onlineOrders.FindAsync(tenant, OnlineOrderId.From(command.OnlineOrderId), cancellationToken);
        if (online is null || !(online.IsAwaitingAcceptance || online.IsAwaitingPayment))
        {
            return;
        }

        var view = await orders.FindAsync(tenant, online.OrderId, cancellationToken);
        if (view is null || view.Status != GuestOrderStatus.Received)
        {
            online.MarkHandled(clock);
        }
        else if (online.IsAwaitingPayment && (await payments.SummaryAsync(tenant, online.OrderId, cancellationToken)).Due <= 0)
        {
            // Paiement encaissé sans que son événement ait encore été traité : on fait comme à sa réception.
            if (online.MarkPaid(clock))
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await orders.AcceptAsync(tenant, online.OrderId, cancellationToken);
                return;
            }
        }
        else if (online.IsOverdue(clock.UtcNow))
        {
            await GuestOrders.CloseAsync(tenant, online, orders, payments, online.IsAwaitingPayment ? UnpaidReason : Reason, cancellationToken);
            online.Expire(clock);
        }
        else
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Le client démarre (ou reprend) le paiement en ligne de sa commande, avant l'échéance.</summary>
/// <param name="ReturnUrl">Page où le prestataire renvoie le client : déclarée pour l'application (sinon refusée) ; null = celle de la plateforme.</param>
public sealed record StartGuestPaymentCommand(string? Token, string? Provider, string? Method = null, string? PayerPhone = null, string? ReturnUrl = null)
    : ICommand<GuestPaymentStartedDto>
{
    public override string ToString() => $"StartGuestPaymentCommand (***, {Provider})";
}

public sealed class StartGuestPaymentHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens,
    IClock clock) : ICommandHandler<StartGuestPaymentCommand, GuestPaymentStartedDto>
{
    public async Task<GuestPaymentStartedDto> HandleAsync(StartGuestPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (app, _, _) = await StorefrontReader.ContextAsync(workspace, client, cancellationToken);
        var (online, view) = await GuestOrders.LoadAsync(workspace, orders, onlineOrders, tokens, command.Token, cancellationToken);
        DomainException.ThrowIf(!GuestOrders.CanPay(online, view, clock.UtcNow), CheckoutErrors.PaymentNotExpected);

        Uri? returnUrl = null;
        if (!string.IsNullOrWhiteSpace(command.ReturnUrl))
        {
            DomainException.ThrowIf(
                !app.AllowsReturnUrl(command.ReturnUrl) || !Uri.TryCreate(command.ReturnUrl, UriKind.Absolute, out returnUrl),
                CheckoutErrors.ReturnUrlNotAllowed);
        }

        var started = await payments.StartAsync(
            workspace.CurrentTenant,
            view.OrderId,
            new OnlinePaymentRequest(command.Provider ?? string.Empty, command.Method, command.PayerPhone, returnUrl),
            cancellationToken);
        return new GuestPaymentStartedDto(started.PaymentId, started.CheckoutUrl);
    }
}

/// <summary>
/// Démo publique des modèles : le visiteur valide le paiement simulé ; la suite (commande prise en charge) est celle d'un
/// vrai paiement. Restaurant de démonstration seulement (403 sinon).
/// </summary>
public sealed record ConfirmDemoPaymentCommand(string? Reference) : ICommand
{
    public override string ToString() => "ConfirmDemoPaymentCommand (***)";
}

public sealed class ConfirmDemoPaymentHandler(StorefrontWorkspace workspace, IStorefrontPayments payments, IDemoTenants? demo = null)
    : ICommandHandler<ConfirmDemoPaymentCommand>
{
    public const string NotDemoCode = "storefront.demo.only";

    public async Task HandleAsync(ConfirmDemoPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (demo is null || !demo.IsDemo(tenant))
        {
            throw new ForbiddenException(new DomainError(NotDemoCode, "Simulated payments only exist in the demonstration."));
        }

        var reference = command.Reference?.Trim() ?? string.Empty;
        DomainException.ThrowIf(reference.Length is 0 or > 200, new DomainError("storefront.demo.reference_invalid", "Unknown payment reference."));
        await payments.CaptureDemoAsync(tenant, reference, cancellationToken);
    }
}

/// <summary>
/// Réaction à un encaissement (par l'outbox, pour le tenant du message) : une commande en ligne qui attendait son
/// paiement et n'a plus rien à payer est prise en charge (acceptée en automatique, mise en attente d'acceptation en
/// manuel). Sans effet pour toute autre commande, et rejouable.
/// </summary>
public sealed record RecordOnlinePaymentCommand(Guid OrderId) : ICommand;

public sealed class RecordOnlinePaymentHandler(
    StorefrontWorkspace workspace,
    IStorefrontOrders orders,
    IStorefrontPayments payments,
    IStorefrontTickets tickets,
    IOnlineOrderRepository onlineOrders,
    IStorefrontUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RecordOnlinePaymentCommand>
{
    public async Task HandleAsync(RecordOnlinePaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var online = command.OrderId == Guid.Empty ? null : await onlineOrders.FindByOrderAsync(tenant, OrderId.From(command.OrderId), cancellationToken);
        if (online is not { IsAwaitingPayment: true } || (await payments.SummaryAsync(tenant, online.OrderId, cancellationToken)).Due > 0)
        {
            return;
        }

        var view = await orders.FindAsync(tenant, online.OrderId, cancellationToken);
        if (view is not { Status: GuestOrderStatus.Received })
        {
            online.MarkHandled(clock);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var accept = online.MarkPaid(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (accept)
        {
            await orders.AcceptAsync(tenant, online.OrderId, cancellationToken);
        }

        // Borne, payée par le QR code : le ticket sort une fois le paiement encaissé.
        if (online.Channel == SalesAppKind.Kiosk)
        {
            await Kiosks.KioskTickets.PrintAsync(workspace, tickets, view, Kiosks.KioskTicketTexts.Paid, clock.UtcNow, cancellationToken);
        }
    }
}

internal static class GuestOrders
{
    /// <summary>La commande du jeton, seulement si elle est du restaurant de la clé et a été passée en ligne ; 404 sinon.</summary>
    public static async Task<(OnlineOrder Online, StorefrontOrderView View)> LoadAsync(
        StorefrontWorkspace workspace,
        IStorefrontOrders orders,
        IOnlineOrderRepository onlineOrders,
        OrderTrackingTokens tokens,
        string? token,
        CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var tracked = tokens.Read(token);
        if (tracked is null || tracked.TenantId != tenant)
        {
            throw new NotFoundException(CheckoutErrors.OrderNotFound);
        }

        var online = await onlineOrders.FindByOrderAsync(tenant, tracked.OrderId, cancellationToken)
            ?? throw new NotFoundException(CheckoutErrors.OrderNotFound);
        var view = await orders.FindAsync(tenant, tracked.OrderId, cancellationToken)
            ?? throw new NotFoundException(CheckoutErrors.OrderNotFound);
        return (online, view);
    }

    public static bool CanCancel(OnlineOrder online, StorefrontOrderView view) =>
        (online.IsAwaitingAcceptance || online.IsAwaitingPayment) && view.Status == GuestOrderStatus.Received;

    public static bool CanPay(OnlineOrder online, StorefrontOrderView view, DateTimeOffset nowUtc) =>
        online.IsAwaitingPayment && !online.IsOverdue(nowUtc) && view.Status == GuestOrderStatus.Received;

    /// <summary>
    /// Annule la commande dans Orders après avoir arrêté l'argent : paiements en attente annulés, et ce qui a été encaissé
    /// en ligne remboursé.
    /// </summary>
    public static async Task CloseAsync(
        TenantId tenant, OnlineOrder online, IStorefrontOrders orders, IStorefrontPayments payments, string reason, CancellationToken cancellationToken)
    {
        if (online.PaymentMode == PaymentMode.Online)
        {
            await payments.CancelPendingAsync(tenant, online.OrderId, reason, cancellationToken);
            if (online.IsPaidOnline || (await payments.SummaryAsync(tenant, online.OrderId, cancellationToken)).Paid > 0)
            {
                await payments.RefundAllAsync(tenant, online.OrderId, reason, cancellationToken);
            }
        }

        await orders.CancelAsync(tenant, online.OrderId, reason, cancellationToken);
    }
}

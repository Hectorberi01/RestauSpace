using System.Globalization;
using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Checkout;

/// <summary>La récompense de fidélité utilisée : son coût en points et sa remise.</summary>
public sealed record CartRewardDto(Guid RewardId, string Name, int PointCost, decimal Discount);

/// <summary>Le coupon tel que le client le voit au devis : accepté avec sa remise, ou refusé avec le motif.</summary>
public sealed record CartCouponDto(string Code, bool IsValid, string? RefusalCode, string? Label, decimal Discount);

/// <summary>
/// Le prix d'un panier tel qu'il sera facturé : lignes, remise du coupon et frais de la zone, calculés par la commande
/// elle-même. <paramref name="ItemsTotal"/> : le total sans les frais (c'est lui que les minimums comparent).
/// </summary>
internal sealed record CheckoutPrice(
    CartQuote Quote, CouponPreview? Coupon, StorefrontDeliveryZone? Zone, decimal ItemsTotal, decimal? Minimum, LoyaltyRewardOption? Reward = null)
{
    public bool MeetsMinimum => Minimum is not { } floor || ItemsTotal >= floor;
}

public static class DeliveryCheckoutErrors
{
    public static readonly DomainError ZoneRequired =
        new("storefront.checkout.delivery_zone_required", "Choose the delivery zone.");
    public static readonly DomainError ZoneUnavailable =
        new("storefront.checkout.delivery_zone_unavailable", "This location does not deliver to this zone.");
    public static readonly DomainError AddressRequired =
        new("storefront.checkout.delivery_address_required", "A name, a phone number and an address are required for a delivery.");
    public static readonly DomainError SlotFull =
        new("storefront.checkout.slot_full", "This time slot is full: choose another time.", DomainErrorKind.Conflict);
    public static readonly DomainError CouponInvalid = new("storefront.checkout.coupon_invalid", "This coupon cannot be used.");
}

internal static class CheckoutPricing
{
    /// <summary>
    /// La livraison proposée par l'établissement pour ce canal : seulement si les réglages en ligne la proposent, que
    /// l'établissement livre (zones actives) et que le canal n'est pas une borne.
    /// </summary>
    public static async Task<StorefrontDeliveryOffer?> OfferAsync(
        TenantId tenant, SalesAppKind channel, OnlineOrderingSettings settings, LocationId locationId, IStorefrontDelivery delivery, CancellationToken cancellationToken)
    {
        if (channel == SalesAppKind.Kiosk || !settings.Offers(ServiceMode.Delivery))
        {
            return null;
        }

        var offer = await delivery.OfferAsync(tenant, locationId, cancellationToken);
        return offer is { Zones.Count: > 0 } ? offer : null;
    }

    /// <param name="zoneRequired">À la validation, une livraison sans zone est refusée ; au devis, elle est chiffrée sans frais.</param>
    public static async Task<CheckoutPrice> PriceAsync(
        TenantId tenant,
        CheckoutContext context,
        ServiceMode mode,
        IReadOnlyList<CartLine> lines,
        string? couponCode,
        Guid? zoneId,
        bool zoneRequired,
        IStorefrontOrders orders,
        IStorefrontCoupons coupons,
        IStorefrontDelivery delivery,
        CancellationToken cancellationToken,
        Guid? rewardId = null,
        CustomerId? customerId = null,
        IStorefrontLoyalty? loyalty = null)
    {
        var locationId = context.Location.LocationId;
        StorefrontDeliveryZone? zone = null;
        if (mode == ServiceMode.Delivery)
        {
            var offer = await OfferAsync(tenant, context.App.Kind, context.Settings, locationId, delivery, cancellationToken)
                ?? throw new DomainException(CheckoutErrors.ServiceModeUnavailable);
            if (zoneId is { } id && id != Guid.Empty)
            {
                zone = offer.Zones.SingleOrDefault(item => item.ZoneId == id) ?? throw new DomainException(DeliveryCheckoutErrors.ZoneUnavailable);
            }

            DomainException.ThrowIf(zone is null && zoneRequired, DeliveryCheckoutErrors.ZoneRequired);
        }

        var quote = await orders.QuoteAsync(tenant, locationId, mode, context.App.Kind, lines, null, cancellationToken);
        CouponPreview? coupon = null;
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            coupon = await coupons.PreviewAsync(tenant, locationId, mode, quote.CurrencyCode, quote.Lines, couponCode, cancellationToken);
        }

        LoyaltyRewardOption? reward = null;
        if (rewardId is { } wanted && wanted != Guid.Empty)
        {
            if (customerId is not { } customer || loyalty is null)
            {
                throw new ForbiddenException(Customers.CustomerAccountErrors.SignInRequired);
            }

            var summary = await loyalty.SummaryAsync(tenant, customer, quote.CurrencyCode, cancellationToken);
            reward = (summary.Enabled ? summary.Rewards.SingleOrDefault(item => item.RewardId == wanted) : null)
                ?? throw new DomainException(Customers.CustomerAccountErrors.RewardUnavailable);
            DomainException.ThrowIf(!reward.Affordable, Customers.CustomerAccountErrors.RewardNotAffordable);
        }

        var discount = coupon is { IsValid: true, PromotionId: { } promotion, Discount: > 0 }
            ? new CartDiscount(promotion, coupon.Discount, coupon.Label ?? coupon.Code)
            : null;
        if (discount is not null || zone is not null || reward is not null)
        {
            quote = await orders.QuoteAsync(
                tenant, locationId, mode, context.App.Kind, lines,
                new CartAdjustments(discount, zone?.Fee, reward is null ? null : new CartReward(reward.RewardId, reward.Discount, reward.Name)),
                cancellationToken);
        }

        var itemsTotal = quote.Total - (quote.DeliveryFee ?? 0m);
        decimal? minimum = (context.Settings.MinimumOrderAmount, zone?.MinimumOrder) switch
        {
            ({ } location, { } zoneMinimum) when zoneMinimum > 0 => Math.Max(location, zoneMinimum),
            (null, { } zoneMinimum) when zoneMinimum > 0 => zoneMinimum,
            ({ } location, _) => location,
            _ => null,
        };
        return new CheckoutPrice(quote, coupon, zone, itemsTotal, minimum, reward);
    }

    public static CartRewardDto? ToDto(LoyaltyRewardOption? reward) =>
        reward is null ? null : new CartRewardDto(reward.RewardId, reward.Name, reward.PointCost, reward.Discount);

    public static CartCouponDto? ToDto(CouponPreview? coupon) =>
        coupon is null ? null : new CartCouponDto(coupon.Code, coupon.IsValid, coupon.RefusalCode, coupon.Label, coupon.IsValid ? coupon.Discount : 0m);
}

/// <summary>Un créneau proposé : l'heure (de retrait, ou de livraison) et s'il reste de la place.</summary>
public sealed record SlotDto(DateTimeOffset StartUtc, bool IsAvailable);

/// <param name="Capacity">Commandes au plus par créneau ; null = sans limite.</param>
public sealed record SlotsDto(Guid LocationId, ServiceMode Mode, string TimeZoneId, int SlotMinutes, int? Capacity, IReadOnlyList<SlotDto> Slots);

/// <summary>
/// Les créneaux d'un établissement servi par l'application, pour les <see cref="OrderSlots.Days"/> prochains jours : dans
/// ses horaires, après le délai de préparation, avec la place restante. En livraison, l'heure proposée est celle de la
/// livraison (préparation + trajet de la zone). L'ouverture fait foi à la validation.
/// </summary>
public sealed record ListSlotsQuery(Guid LocationId, ServiceMode Mode, Guid? ZoneId = null) : IQuery<SlotsDto>;

public sealed class ListSlotsHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontDelivery delivery, IOnlineOrderRepository onlineOrders, IClock clock)
    : IQueryHandler<ListSlotsQuery, SlotsDto>
{
    public async Task<SlotsDto> HandleAsync(ListSlotsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.UtcNow;
        var context = await CheckoutReader.ContextAsync(workspace, client, query.LocationId, now, cancellationToken);
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(!Enum.IsDefined(query.Mode) || !context.Settings.Offers(query.Mode), CheckoutErrors.ServiceModeUnavailable);

        var travel = 0;
        if (query.Mode == ServiceMode.Delivery)
        {
            var offer = await CheckoutPricing.OfferAsync(tenant, context.App.Kind, context.Settings, context.Location.LocationId, delivery, cancellationToken)
                ?? throw new DomainException(CheckoutErrors.ServiceModeUnavailable);
            if (query.ZoneId is { } zoneId && zoneId != Guid.Empty)
            {
                travel = (offer.Zones.SingleOrDefault(zone => zone.ZoneId == zoneId) ?? throw new DomainException(DeliveryCheckoutErrors.ZoneUnavailable))
                    .EstimatedMinutes;
            }
        }

        var ready = OrderSlotPlanner.ReadySlots(context.Location, now.AddMinutes(context.Settings.PreparationMinutes), now.AddDays(OrderSlots.Days));
        var capacity = context.Settings.SlotCapacity;
        var taken = capacity is null || ready.Count == 0
            ? new Dictionary<DateTimeOffset, int>()
            : await onlineOrders.CountBySlotAsync(tenant, context.Location.LocationId, ready[0], ready[^1].AddMinutes(OrderSlots.Minutes), cancellationToken);
        var open = context.Enabled && !context.Settings.IsPaused && context.Location.IsOperational;
        return new SlotsDto(
            query.LocationId,
            query.Mode,
            context.Location.TimeZoneId,
            OrderSlots.Minutes,
            capacity,
            ready.Select(slot => new SlotDto(slot.AddMinutes(travel), open && (capacity is not { } max || taken.GetValueOrDefault(slot) < max))).ToList());
    }
}

/// <summary>Le calcul des créneaux d'après les horaires de la semaine, dans le fuseau de l'établissement.</summary>
internal static class OrderSlotPlanner
{
    public const int MaxSlots = 7 * 24 * 4;

    /// <summary>Les débuts de créneau (UTC) où la commande peut être prête, de <paramref name="fromUtc"/> à <paramref name="untilUtc"/>.</summary>
    public static IReadOnlyList<DateTimeOffset> ReadySlots(StorefrontLocation location, DateTimeOffset fromUtc, DateTimeOffset untilUtc)
    {
        var zone = TimeZone(location.TimeZoneId);
        var localFrom = TimeZoneInfo.ConvertTime(fromUtc, zone);
        var slots = new SortedSet<DateTimeOffset>();
        for (var day = -1; day <= OrderSlots.Days; day++)
        {
            var date = DateOnly.FromDateTime(localFrom.Date).AddDays(day);
            foreach (var range in location.WeeklyHours.Where(range => range.Day == date.DayOfWeek))
            {
                if (!TimeOnly.TryParse(range.Start, CultureInfo.InvariantCulture, out var start)
                    || !TimeOnly.TryParse(range.End, CultureInfo.InvariantCulture, out var end))
                {
                    continue;
                }

                var opens = Utc(date.ToDateTime(start), zone);
                var closes = Utc((range.EndsNextDay ? date.AddDays(1) : date).ToDateTime(end), zone);
                for (var slot = OrderSlots.NextFrom(opens); slot < closes; slot = slot.AddMinutes(OrderSlots.Minutes))
                {
                    if (slot >= fromUtc && slot < untilUtc)
                    {
                        slots.Add(slot);
                    }
                }
            }
        }

        return slots.Take(MaxSlots).ToList();
    }

    private static DateTimeOffset Utc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static TimeZoneInfo TimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

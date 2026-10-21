using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class DeliveryCouponTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();
    private readonly StorefrontDeliveryZone _cadjehoun = new(Guid.NewGuid(), "Cadjèhoun", 1000m, 6000m, 25);
    private readonly Guid _promotion = Guid.NewGuid();

    public DeliveryCouponTests()
    {
        _fixture.Prices[_burger] = 4000m;
        _fixture.Coupons["BIENVENUE"] = (_promotion, 1500m, null);
        _fixture.Coupons["EPUISE"] = (Guid.NewGuid(), 500m, "promotions.coupon.exhausted");
    }

    private static readonly GuestContactInput Awa = new("Awa", Phone: "+22990000001");

    private IReadOnlyList<CartLine> Cart(int burgers = 2) => [new CartLine(_burger, burgers)];

    private async Task SignedInAsync(SalesAppKind kind = SalesAppKind.Web, int? capacity = null, decimal? minimum = null)
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.Options.Add((_fixture.TenantA, kind));
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway, ServiceMode.Delivery], 20, minimum, 10, 15,
            [new ChannelPolicyInput(kind, true, true, null, OrderAcceptance.Automatic)], capacity));
        _fixture.DeliveryOffers[_fixture.Cotonou] = new StorefrontDeliveryOffer(15, [_cadjehoun]);
        var issued = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("App", kind));
        await _fixture.SignInAsync(issued.Key);
    }

    private Task<GuestOrderPlacedDto> DeliverAsync(
        DeliveryAddressInput? address, GuestContactInput? customer = null, string? coupon = null, decimal? expected = null, DateTimeOffset? at = null) =>
        _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Delivery, Cart(), PaymentMode.Counter, customer ?? Awa, at, expected, coupon, address));

    [Fact]
    public async Task The_delivery_quote_adds_the_zone_fee_and_checks_the_zone_minimum_without_the_fee()
    {
        await SignedInAsync(minimum: 2000m);

        var withoutZone = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Delivery, Cart()));
        var quote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Delivery, Cart(), DeliveryZoneId: _cadjehoun.ZoneId));
        var small = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Delivery, Cart(1), DeliveryZoneId: _cadjehoun.ZoneId));
        var unknown = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Delivery, Cart(), DeliveryZoneId: Guid.NewGuid())));

        Assert.Equal((8000m, (decimal?)null, (decimal?)2000m), (withoutZone.Total, withoutZone.DeliveryFee, withoutZone.MinimumOrderAmount));
        Assert.Equal((9000m, (decimal?)1000m, (Guid?)_cadjehoun.ZoneId, (decimal?)6000m, true), (quote.Total, quote.DeliveryFee, quote.DeliveryZoneId, quote.MinimumOrderAmount, quote.MeetsMinimum));
        Assert.Equal((5000m, false), (small.Total, small.MeetsMinimum));
        Assert.Equal(DeliveryCheckoutErrors.ZoneUnavailable, unknown.Error);
    }

    [Fact]
    public async Task A_delivery_order_creates_its_delivery_sheet_with_the_fee_and_the_customer_follows_it_without_the_address()
    {
        await SignedInAsync();
        var at = _fixture.Clock.UtcNow.AddHours(2);

        var noZone = await Assert.ThrowsAsync<DomainException>(() => DeliverAsync(null));
        var noAddress = await Assert.ThrowsAsync<DomainException>(() => DeliverAsync(new DeliveryAddressInput(_cadjehoun.ZoneId, " ")));
        var noPhone = await Assert.ThrowsAsync<DomainException>(() =>
            DeliverAsync(new DeliveryAddressInput(_cadjehoun.ZoneId, "Rue 12"), new GuestContactInput("Awa", Email: "awa@example.com")));
        var placed = await DeliverAsync(new DeliveryAddressInput(_cadjehoun.ZoneId, " Rue 12, maison bleue ", "Face à la pharmacie"), expected: 9000m, at: at);
        var tracked = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken));

        Assert.Equal(DeliveryCheckoutErrors.ZoneRequired, noZone.Error);
        Assert.Equal(DeliveryCheckoutErrors.AddressRequired, noAddress.Error);
        Assert.Equal(DeliveryCheckoutErrors.AddressRequired, noPhone.Error);
        Assert.Equal(9000m, placed.Total);
        var sheet = _fixture.Deliveries[OrderId.From(placed.OrderId)];
        Assert.Equal((_cadjehoun.ZoneId, "Awa", "+22990000001", "Rue 12, maison bleue", "Face à la pharmacie", (DateTimeOffset?)at),
            (sheet.ZoneId, sheet.CustomerName, sheet.CustomerPhone, sheet.AddressLine, sheet.Landmark, sheet.DeliverAtUtc));
        Assert.DoesNotContain("Rue 12", sheet.ToString(), StringComparison.Ordinal);
        Assert.Equal(ServiceMode.Delivery, tracked.Mode);
        Assert.Equal(new GuestDeliveryDto("Cadjèhoun", 1000m, "Pending", _fixture.Clock.UtcNow.AddMinutes(25)), tracked.Delivery);
        Assert.Equal(OrderSlots.StartOf(at.AddMinutes(-25)), Assert.Single(_fixture.OnlineOrders).SlotStartUtc);
    }

    [Fact]
    public async Task A_location_that_does_not_deliver_takes_no_delivery_order()
    {
        await SignedInAsync();
        _fixture.DeliveryOffers.Clear();

        var notDelivering = await Assert.ThrowsAsync<DomainException>(() => DeliverAsync(new DeliveryAddressInput(_cadjehoun.ZoneId, "Rue 12")));

        Assert.Equal(CheckoutErrors.ServiceModeUnavailable, notDelivering.Error);
        Assert.Empty(_fixture.Orders);
    }

    [Fact]
    public async Task A_coupon_is_shown_in_the_quote_and_counted_when_the_order_is_placed()
    {
        await SignedInAsync();

        var quote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), " bienvenue "));
        var refusedQuote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), "EPUISE"));
        var unknownQuote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), "INCONNU"));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Counter, Awa, CouponCode: "EPUISE")));
        var placed = await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Counter, Awa, ExpectedTotal: 6500m, CouponCode: "bienvenue"));
        var tracked = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken));

        Assert.Equal((6500m, 1500m), (quote.Total, quote.DiscountTotal));
        Assert.Equal(new CartCouponDto("BIENVENUE", true, null, "Promo BIENVENUE", 1500m), quote.Coupon);
        Assert.Equal((8000m, "promotions.coupon.exhausted", false), (refusedQuote.Total, refusedQuote.Coupon!.RefusalCode, refusedQuote.Coupon.IsValid));
        Assert.Equal("promotions.coupon.not_found", unknownQuote.Coupon!.RefusalCode);
        Assert.Equal("promotions.coupon.exhausted", refused.Code);
        Assert.Equal(6500m, placed.Total);
        Assert.Equal(("BIENVENUE", OrderId.From(placed.OrderId)), Assert.Single(_fixture.RedeemedCoupons));
        Assert.Equal(1500m, tracked.DiscountTotal);
    }

    [Fact]
    public async Task A_coupon_and_a_delivery_fee_add_up_and_the_counter_limit_sees_the_full_total()
    {
        await SignedInAsync();

        var placed = await DeliverAsync(new DeliveryAddressInput(_cadjehoun.ZoneId, "Rue 12"), coupon: "BIENVENUE", expected: 7500m);

        Assert.Equal(7500m, placed.Total);
        Assert.Equal((1500m, (decimal?)1000m), _fixture.OrderAdjustments[OrderId.From(placed.OrderId)]);
    }

    [Fact]
    public async Task A_full_slot_refuses_new_orders_and_frees_its_place_when_the_customer_cancels()
    {
        await SignedInAsync(capacity: 1);
        var takeaway = new PlaceGuestOrderCommand(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Online, Awa);
        _fixture.PaymentOptions[_fixture.TenantA] = [new OnlinePaymentOption("fedapay", "FedaPay", ["Card"])];

        var first = await _fixture.Place.HandleAsync(takeaway);
        var full = await Assert.ThrowsAsync<DomainException>(() => _fixture.Place.HandleAsync(takeaway));
        await _fixture.Cancel.HandleAsync(new CancelGuestOrderCommand(first.TrackingToken));
        var second = await _fixture.Place.HandleAsync(takeaway);

        Assert.Equal(DeliveryCheckoutErrors.SlotFull, full.Error);
        Assert.NotEqual(first.OrderId, second.OrderId);
    }

    [Fact]
    public async Task Slots_follow_the_opening_hours_in_the_location_time_zone_and_show_the_full_ones()
    {
        await SignedInAsync(capacity: 1);
        var monday = new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero);
        _fixture.PaymentOptions[_fixture.TenantA] = [new OnlinePaymentOption("fedapay", "FedaPay", ["Card"])];
        await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, Cart(), PaymentMode.Online, Awa, monday.AddMinutes(30)));

        var pickup = await _fixture.Slots.HandleAsync(new ListSlotsQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway));
        var delivery = await _fixture.Slots.HandleAsync(new ListSlotsQuery(_fixture.Cotonou.Value, ServiceMode.Delivery, _cadjehoun.ZoneId));

        // Lundi 11:00 à 22:00 à Cotonou (UTC+1) : de 10:00 à 20:45 UTC, 44 créneaux.
        Assert.Equal(("Africa/Porto-Novo", 15, (int?)1, 44), (pickup.TimeZoneId, pickup.SlotMinutes, pickup.Capacity, pickup.Slots.Count));
        Assert.Equal((monday, monday.AddHours(10).AddMinutes(45)), (pickup.Slots[0].StartUtc, pickup.Slots[^1].StartUtc));
        Assert.Equal([monday.AddMinutes(30)], pickup.Slots.Where(slot => !slot.IsAvailable).Select(slot => slot.StartUtc));
        Assert.Equal(monday.AddMinutes(25), delivery.Slots[0].StartUtc);
    }
}

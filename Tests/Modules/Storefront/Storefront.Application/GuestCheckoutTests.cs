using Storefront.Application.Queries;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Commands;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class GuestCheckoutTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();
    private readonly Guid _drink = Guid.NewGuid();

    public GuestCheckoutTests()
    {
        _fixture.Prices[_burger] = 4000m;
        _fixture.Prices[_drink] = 500m;
    }

    private static readonly GuestContactInput Awa = new("Awa", Phone: "+22990000001", MarketingConsent: true);

    private IReadOnlyList<CartLine> Cart(int burgers = 1) => [new CartLine(_burger, burgers), new CartLine(_drink, 2, Note: "Bien frais")];

    private async Task<IssuedSalesAppKey> SignedInAsync(SalesAppKind kind = SalesAppKind.Web)
    {
        _fixture.Options.Add((_fixture.TenantA, kind));
        var issued = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("App", kind));
        await _fixture.SignInAsync(issued.Key);
        return issued;
    }

    private Task SettingsAsync(OrderAcceptance acceptance = OrderAcceptance.Automatic, decimal? minimum = null, decimal? counterLimit = null, bool payAtCounter = true)
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        return _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway, ServiceMode.DineIn, ServiceMode.Delivery], 20, minimum, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Web, true, payAtCounter, counterLimit, acceptance), new ChannelPolicyInput(SalesAppKind.Kiosk, true, true, null, acceptance)]));
    }

    private Task<GuestOrderPlacedDto> PlaceAsync(
        GuestContactInput? customer = null, decimal? expected = null, DateTimeOffset? scheduled = null, ServiceMode mode = ServiceMode.Takeaway,
        PaymentMode payment = PaymentMode.Counter, Guid? location = null) =>
        _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            location ?? _fixture.Cotonou.Value, mode, Cart(), payment, customer ?? Awa, scheduled, expected));

    [Fact]
    public async Task The_quote_is_computed_by_the_server_and_tells_what_the_customer_can_pay_with()
    {
        await SettingsAsync(minimum: 6000m, counterLimit: 4000m);
        await SignedInAsync();
        _fixture.PaymentOptions[_fixture.TenantA] = [new OnlinePaymentOption("fedapay", "FedaPay", ["Card"])];

        var quote = await _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, Cart()));
        var empty = await Assert.ThrowsAsync<DomainException>(() => _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, [])));
        var tooMany = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Quote.HandleAsync(new QuoteCartQuery(_fixture.Cotonou.Value, ServiceMode.Takeaway, [new CartLine(_burger, 100)])));

        Assert.Equal((5000m, "XOF"), (quote.Total, quote.CurrencyCode));
        Assert.False(quote.MeetsMinimum);
        Assert.False(quote.CanPayAtCounter);
        Assert.True(quote.CanPayOnline);
        Assert.Equal(CheckoutErrors.CartEmpty, empty.Error);
        Assert.Equal(CheckoutErrors.CartTooLarge, tooMany.Error);
    }

    [Fact]
    public async Task An_automatically_accepted_order_goes_to_the_kitchen_for_the_app_channel_and_its_guest()
    {
        await SettingsAsync();
        var app = await SignedInAsync();

        var placed = await PlaceAsync(expected: 5000m);

        var (tenant, order, status, total, _) = _fixture.Orders[OrderId.From(placed.OrderId)];
        Assert.Equal(_fixture.TenantA, tenant);
        Assert.Equal((SalesAppKind.Web, app.Id, ServiceMode.Takeaway), (order.Channel, order.AppId.Value, order.Mode));
        Assert.Equal(_fixture.Customers[(_fixture.TenantA, "+22990000001")], order.CustomerId);
        Assert.True(Assert.Single(_fixture.CustomerRequests).MarketingConsent);
        Assert.Equal((GuestOrderStatus.Accepted, 5000m), (status, total));
        Assert.Equal((GuestOrderStatus.Accepted, PaymentMode.Counter, (DateTimeOffset?)null, (DateTimeOffset?)null), (placed.Status, placed.Payment, placed.AcceptBeforeUtc, placed.PayBeforeUtc));
        var online = Assert.Single(_fixture.OnlineOrders);
        Assert.Equal(OnlineOrderState.Handled, online.State);
        Assert.Equal(new TrackedOrder(_fixture.TenantA, OrderId.From(placed.OrderId)), _fixture.Tokens.Read(placed.TrackingToken));
        Assert.DoesNotContain("+229", placed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manual_order_waits_for_the_staff_then_is_refused_once_its_deadline_passed()
    {
        await SettingsAsync(OrderAcceptance.Manual);
        await SignedInAsync();

        var placed = await PlaceAsync();
        var online = Assert.Single(_fixture.OnlineOrders);
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(online.Id.Value));
        var stillWaiting = _fixture.Orders[OrderId.From(placed.OrderId)].Status;
        _fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(online.Id.Value));

        Assert.Equal(GuestOrderStatus.Received, placed.Status);
        Assert.Equal(FakeClock.DefaultNow.AddMinutes(10), placed.AcceptBeforeUtc);
        Assert.Equal(GuestOrderStatus.Received, stillWaiting);
        Assert.Equal((GuestOrderStatus.Cancelled, ResolveOverdueOnlineOrderHandler.Reason), (_fixture.Orders[online.OrderId].Status, _fixture.Orders[online.OrderId].CancelReason));
        Assert.Equal(OnlineOrderState.Expired, online.State);
    }

    [Fact]
    public async Task An_order_accepted_by_the_staff_in_time_is_no_longer_watched()
    {
        await SettingsAsync(OrderAcceptance.Manual);
        await SignedInAsync();
        var placed = await PlaceAsync();
        var id = OrderId.From(placed.OrderId);
        _fixture.Orders[id] = _fixture.Orders[id] with { Status = GuestOrderStatus.Accepted };
        _fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        _fixture.CurrentTenantId = _fixture.TenantA;

        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(_fixture.OnlineOrders[0].Id.Value));

        Assert.Equal(GuestOrderStatus.Accepted, _fixture.Orders[id].Status);
        Assert.Equal(OnlineOrderState.Handled, _fixture.OnlineOrders[0].State);
    }

    [Fact]
    public async Task The_guest_follows_the_order_and_cancels_it_only_before_acceptance()
    {
        await SettingsAsync(OrderAcceptance.Manual);
        await SignedInAsync();
        var waiting = await PlaceAsync();
        await SettingsAsync(OrderAcceptance.Automatic);
        await _fixture.SignInAsync((await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site 2", SalesAppKind.Web))).Key);
        var accepted = await PlaceAsync();

        var tracked = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(waiting.TrackingToken));
        await _fixture.Cancel.HandleAsync(new CancelGuestOrderCommand(waiting.TrackingToken));
        var cancelled = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(waiting.TrackingToken));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Cancel.HandleAsync(new CancelGuestOrderCommand(accepted.TrackingToken)));

        Assert.True(tracked.CanCancel);
        Assert.Equal(("Cotonou", 5000m, PaymentMode.Counter), (tracked.LocationName, tracked.AmountDueAtCounter, tracked.Payment));
        Assert.Equal(FakeClock.DefaultNow.AddMinutes(20), tracked.EstimatedReadyAtUtc);
        Assert.Equal(GuestOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, cancelled.AmountDueAtCounter);
        Assert.False(cancelled.CanCancel);
        Assert.Equal(CancelGuestOrderHandler.Reason, _fixture.Orders[OrderId.From(waiting.OrderId)].CancelReason);
        Assert.Equal(OnlineOrderErrors.NotCancellable, refused.Error);
    }

    [Fact]
    public async Task A_tracking_token_opens_its_own_order_only_with_a_key_of_the_same_restaurant()
    {
        await SettingsAsync();
        await SignedInAsync();
        var placed = await PlaceAsync();
        var otherRestaurant = _fixture.Tokens.Issue(_fixture.TenantB, OrderId.From(placed.OrderId));
        var unknown = _fixture.Tokens.Issue(_fixture.TenantA, OrderId.New());

        var forged = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken[..^2] + "AA")));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Track.HandleAsync(new GetGuestOrderQuery(otherRestaurant)));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Track.HandleAsync(new GetGuestOrderQuery(unknown)));

        Assert.All(new[] { forged, foreign, missing }, error => Assert.Equal(CheckoutErrors.OrderNotFound, error.Error));
    }

    [Fact]
    public async Task The_server_refuses_what_the_restaurant_does_not_offer()
    {
        await SettingsAsync(minimum: 6000m);
        await SignedInAsync();
        var belowMinimum = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());
        await SettingsAsync(counterLimit: 3000m);
        var overCounterLimit = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());
        await SettingsAsync(payAtCounter: false);
        var noCounter = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());
        await SettingsAsync();
        var online = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(payment: PaymentMode.Online, customer: null));
        var delivery = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(mode: ServiceMode.Delivery));
        var priceChanged = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(expected: 4500m));
        var noContact = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(new GuestContactInput("Awa")));
        var closed = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(location: _fixture.Calavi.Value));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => PlaceAsync(location: _fixture.Paris.Value));
        await _fixture.Pause.HandleAsync(new PauseOnlineOrderingCommand(_fixture.Cotonou.Value, true));
        var paused = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());

        Assert.Equal(CheckoutErrors.BelowMinimum, belowMinimum.Error);
        Assert.Equal(CheckoutErrors.CounterLimitExceeded, overCounterLimit.Error);
        Assert.Equal(CheckoutErrors.PaymentModeNotAllowed, noCounter.Error);
        Assert.Equal(CheckoutErrors.OnlinePaymentUnavailable, online.Error);
        Assert.Equal(CheckoutErrors.ServiceModeUnavailable, delivery.Error);
        Assert.Equal(CheckoutErrors.PriceChanged, priceChanged.Error);
        Assert.Equal(CheckoutErrors.CustomerRequired, noContact.Error);
        Assert.Equal(CheckoutErrors.Closed, closed.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(CheckoutErrors.Paused, paused.Error);
        Assert.Empty(_fixture.Orders);
        Assert.Empty(_fixture.CustomerRequests);
    }

    [Fact]
    public async Task Without_the_option_nothing_is_ordered_and_a_refusal_by_orders_records_nothing()
    {
        await SettingsAsync();
        await SignedInAsync();
        _fixture.Options.Clear();
        var withoutOption = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());
        _fixture.Options.Add((_fixture.TenantA, SalesAppKind.Web));
        _fixture.OrdersRefusePlacing = true;
        var refused = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync());

        Assert.Equal(CheckoutErrors.Unavailable, withoutOption.Error);
        Assert.Equal(StorefrontFixture.PlacingRefused, refused.Error);
        Assert.Empty(_fixture.OnlineOrders);
    }

    [Fact]
    public async Task A_scheduled_order_must_leave_time_to_prepare_and_fall_within_opening_hours()
    {
        await SettingsAsync();
        await SignedInAsync();
        var evening = FakeClock.DefaultNow.AddHours(5);
        _fixture.OpenAt = (_, at) => at < FakeClock.DefaultNow.AddHours(6);

        var tooSoon = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(scheduled: FakeClock.DefaultNow.AddMinutes(10)));
        var night = await Assert.ThrowsAsync<DomainException>(() => PlaceAsync(scheduled: FakeClock.DefaultNow.AddHours(8)));
        var placed = await PlaceAsync(scheduled: evening);
        _fixture.OpenAt = (_, at) => at > FakeClock.DefaultNow.AddHours(1);
        var laterWhileClosed = await PlaceAsync(scheduled: evening);

        Assert.Equal(CheckoutErrors.ScheduleTooSoon, tooSoon.Error);
        Assert.Equal(CheckoutErrors.ClosedAtSchedule, night.Error);
        Assert.Equal(evening, _fixture.Orders[OrderId.From(placed.OrderId)].Order.ScheduledForUtc);
        Assert.NotNull(laterWhileClosed);
    }

    [Fact]
    public async Task A_review_is_left_on_a_completed_order_of_the_token_only()
    {
        await SettingsAsync();
        await SignedInAsync();
        var placed = await PlaceAsync();
        var reviews = new RecordingReviews();
        var handler = new SubmitStorefrontReviewHandler(_fixture.Workspace, _fixture, reviews, _fixture, _fixture, _fixture.Tokens);

        var early = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(new SubmitStorefrontReviewCommand(placed.TrackingToken, 5, "Top")));
        var orderId = OrderId.From(placed.OrderId);
        _fixture.Orders[orderId] = _fixture.Orders[orderId] with { Status = GuestOrderStatus.Completed };
        await handler.HandleAsync(new SubmitStorefrontReviewCommand(placed.TrackingToken, 5, "Top"));
        var forged = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(new SubmitStorefrontReviewCommand(placed.TrackingToken + "x", 5, null)));

        Assert.Equal(SubmitStorefrontReviewHandler.OrderNotCompleted, early.Error);
        Assert.Equal((_fixture.TenantA, _fixture.Cotonou, orderId, 5, "Top"), Assert.Single(reviews.Submitted));
        Assert.Equal(CheckoutErrors.OrderNotFound, forged.Error);
    }

    private sealed class RecordingReviews : IStorefrontReviews
    {
        public List<(TenantId, LocationId, OrderId, int, string?)> Submitted { get; } = [];

        public Task<StorefrontReviews> GetAsync(TenantId tenantId, IReadOnlyList<LocationId>? locations, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorefrontReviews(0, null, [], []));

        public Task<Guid> SubmitAsync(TenantId tenantId, LocationId locationId, OrderId orderId, int rating, string? comment, CancellationToken cancellationToken = default)
        {
            Submitted.Add((tenantId, locationId, orderId, rating, comment));
            return Task.FromResult(Guid.NewGuid());
        }
    }
}

public sealed class GuestOnlinePaymentTests
{
    private readonly StorefrontFixture _fixture = new();
    private readonly Guid _burger = Guid.NewGuid();

    public GuestOnlinePaymentTests()
    {
        _fixture.Prices[_burger] = 4000m;
        _fixture.PaymentOptions[_fixture.TenantA] = [new OnlinePaymentOption("fedapay", "FedaPay", ["MobileMoney", "Card"])];
    }

    private async Task<GuestOrderPlacedDto> PlaceOnlineAsync(OrderAcceptance acceptance = OrderAcceptance.Automatic)
    {
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway], 20, null, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Web, true, false, null, acceptance)]));
        var app = await _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Site", SalesAppKind.Web, [], [], ["https://chezawa.bj/merci"]));
        await _fixture.SignInAsync(app.Key);
        return await _fixture.Place.HandleAsync(new PlaceGuestOrderCommand(
            _fixture.Cotonou.Value, ServiceMode.Takeaway, [new CartLine(_burger, 1)], PaymentMode.Online, new GuestContactInput("Awa", "awa@exemple.bj")));
    }

    private OrderId OrderOf(GuestOrderPlacedDto placed) => OrderId.From(placed.OrderId);

    [Fact]
    public async Task An_order_paid_online_waits_for_its_payment_then_goes_to_the_kitchen()
    {
        var placed = await PlaceOnlineAsync();

        var before = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken));
        var started = await _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(
            placed.TrackingToken, "fedapay", "MobileMoney", ReturnUrl: $"https://chezawa.bj/merci?commande={placed.TrackingToken}"));
        var processing = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var waiting = await _fixture.Waiting.HandleAsync(new ListWaitingOnlineOrdersQuery(_fixture.Cotonou.Value));
        _fixture.Capture(OrderOf(placed));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.RecordPayment.HandleAsync(new RecordOnlinePaymentCommand(placed.OrderId));
        await _fixture.RecordPayment.HandleAsync(new RecordOnlinePaymentCommand(placed.OrderId));
        var paid = await _fixture.Track.HandleAsync(new GetGuestOrderQuery(placed.TrackingToken));

        Assert.Equal((GuestOrderStatus.Received, FakeClock.DefaultNow.AddMinutes(15)), (placed.Status, placed.PayBeforeUtc!.Value));
        Assert.Equal((GuestPaymentState.AwaitingPayment, true, 0m), (before.PaymentState, before.CanPay, before.AmountDueAtCounter));
        Assert.Equal("fedapay", Assert.Single(before.PaymentOptions).Provider);
        Assert.NotNull(started.CheckoutUrl);
        Assert.Equal(new Uri($"https://chezawa.bj/merci?commande={placed.TrackingToken}"), _fixture.Payments[0].ReturnUrl);
        Assert.Equal(GuestPaymentState.Processing, processing.PaymentState);
        Assert.Equal((OnlineOrderState.AwaitingPayment, FakeClock.DefaultNow.AddMinutes(15)), (waiting[0].State, waiting[0].Deadline));
        Assert.Equal((GuestPaymentState.Paid, 4000m, false), (paid.PaymentState, paid.AmountPaid, paid.CanPay));
        Assert.Equal(GuestOrderStatus.Accepted, _fixture.Orders[OrderOf(placed)].Status);
        Assert.Empty(await _fixture.Waiting.HandleAsync(new ListWaitingOnlineOrdersQuery(_fixture.Cotonou.Value)));
    }

    [Fact]
    public async Task A_manual_order_paid_online_is_refunded_when_nobody_accepts_it()
    {
        var placed = await PlaceOnlineAsync(OrderAcceptance.Manual);
        await _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay"));
        _fixture.Capture(OrderOf(placed));
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await _fixture.RecordPayment.HandleAsync(new RecordOnlinePaymentCommand(placed.OrderId));
        var online = Assert.Single(_fixture.OnlineOrders);
        var waiting = online.State;
        _fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(online.Id.Value));

        Assert.Equal(OnlineOrderState.AwaitingAcceptance, waiting);
        Assert.Equal(OnlineOrderState.Expired, online.State);
        Assert.Equal((GuestOrderStatus.Cancelled, ResolveOverdueOnlineOrderHandler.Reason), (_fixture.Orders[OrderOf(placed)].Status, _fixture.Orders[OrderOf(placed)].CancelReason));
        Assert.Equal("Refunded", Assert.Single(_fixture.Payments).Status);
    }

    [Fact]
    public async Task An_unpaid_order_is_cancelled_at_its_deadline_with_its_pending_payment()
    {
        var placed = await PlaceOnlineAsync();
        await _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay"));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        var late = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay")));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(_fixture.OnlineOrders[0].Id.Value));

        Assert.Equal(CheckoutErrors.PaymentNotExpected, late.Error);
        Assert.Equal(ResolveOverdueOnlineOrderHandler.UnpaidReason, _fixture.Orders[OrderOf(placed)].CancelReason);
        Assert.Equal("Cancelled", Assert.Single(_fixture.Payments).Status);
        Assert.Equal(OnlineOrderState.Expired, _fixture.OnlineOrders[0].State);
    }

    [Fact]
    public async Task A_payment_captured_before_its_event_is_seen_by_the_job_and_not_cancelled()
    {
        var placed = await PlaceOnlineAsync();
        await _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay"));
        _fixture.Capture(OrderOf(placed));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        _fixture.CurrentTenantId = _fixture.TenantA;

        await _fixture.Resolve.HandleAsync(new ResolveOverdueOnlineOrderCommand(_fixture.OnlineOrders[0].Id.Value));

        Assert.Equal(GuestOrderStatus.Accepted, _fixture.Orders[OrderOf(placed)].Status);
        Assert.Equal(OnlineOrderState.Handled, _fixture.OnlineOrders[0].State);
    }

    [Fact]
    public async Task The_return_page_must_be_declared_and_a_counter_order_takes_no_online_payment()
    {
        var placed = await PlaceOnlineAsync();
        var foreign = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay", ReturnUrl: "https://evil.example/merci")));
        await _fixture.Cancel.HandleAsync(new CancelGuestOrderCommand(placed.TrackingToken));
        var cancelled = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartPayment.HandleAsync(new StartGuestPaymentCommand(placed.TrackingToken, "fedapay")));

        Assert.Equal(CheckoutErrors.ReturnUrlNotAllowed, foreign.Error);
        Assert.Equal(CheckoutErrors.PaymentNotExpected, cancelled.Error);
        Assert.Equal(CancelGuestOrderHandler.Reason, _fixture.Orders[OrderOf(placed)].CancelReason);
        Assert.Empty(_fixture.Payments);
    }
}

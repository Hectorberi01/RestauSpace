namespace Storefront.Domain;

public sealed class SalesAppTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private static PublishableKeyHash Hash(char c) => PublishableKeyHash.Create(new string(c, PublishableKeyHash.Length));

    private SalesApp Web() => SalesApp.Create(Tenant, " Site de Chez Awa ", SalesAppKind.Web, Hash('a'), "Ab3_", _clock);

    [Fact]
    public void A_website_accepts_only_its_declared_origins_normalized()
    {
        var app = Web();

        app.SetAllowedOrigins(["https://ChezAwa.bj/", "https://chezawa.bj", "http://localhost:3002"], _clock);

        Assert.Equal("Site de Chez Awa", app.Name);
        Assert.Equal(["http://localhost:3002", "https://chezawa.bj"], app.AllowedOrigins);
        Assert.True(app.AcceptsOrigin("https://chezawa.bj"));
        Assert.True(app.AcceptsOrigin(null));
        Assert.False(app.AcceptsOrigin("https://evil.example"));
        Assert.False(app.AcceptsOrigin("https://chezawa.bj.evil.example"));
        DomainAssert.Throws(SalesAppErrors.OriginInvalid, () => app.SetAllowedOrigins(["http://chezawa.bj"], _clock));
        DomainAssert.Throws(SalesAppErrors.OriginInvalid, () => app.SetAllowedOrigins(["https://chezawa.bj/menu"], _clock));
    }

    [Fact]
    public void A_mobile_app_or_a_kiosk_accepts_a_browser_origin_only_when_declared()
    {
        var kiosk = SalesApp.Create(Tenant, "Borne", SalesAppKind.Kiosk, Hash('b'), "abcd", _clock);
        var mobile = SalesApp.Create(Tenant, "Appli", SalesAppKind.MobileApp, Hash('c'), "abcd", _clock);

        Assert.True(kiosk.AcceptsOrigin(null));
        Assert.False(kiosk.AcceptsOrigin("https://borne.chezawa.bj"));
        kiosk.SetAllowedOrigins(["https://borne.chezawa.bj"], _clock);
        mobile.SetAllowedOrigins(["capacitor://localhost", "https://localhost"], _clock);

        Assert.True(kiosk.AcceptsOrigin("https://borne.chezawa.bj"));
        Assert.False(kiosk.AcceptsOrigin("https://chezawa.bj"));
        Assert.True(mobile.AcceptsOrigin("capacitor://localhost"));
        Assert.False(mobile.AcceptsOrigin("ionic://localhost"));
        DomainAssert.Throws(SalesAppErrors.OriginInvalid, () => kiosk.SetAllowedOrigins(["capacitor://localhost"], _clock));
        DomainAssert.Throws(SalesAppErrors.OriginInvalid, () => mobile.SetAllowedOrigins(["capacitor://chezawa.bj"], _clock));
    }

    [Fact]
    public void Locations_come_from_the_same_restaurant_and_none_means_all()
    {
        var app = Web();
        var mine = new LocationReference(Tenant, LocationId.New());
        var other = new LocationReference(TenantId.New(), LocationId.New());

        Assert.True(app.Serves(LocationId.New()));
        app.SetLocations([mine], _clock);
        Assert.True(app.Serves(mine.LocationId));
        Assert.False(app.Serves(LocationId.New()));
        Assert.ThrowsAny<Exception>(() => app.SetLocations([other], _clock));
        Assert.Equal([mine.LocationId], app.LocationIds);
    }

    [Fact]
    public void Return_pages_are_https_and_compared_exactly()
    {
        var app = Web();

        app.SetReturnUrls(["https://chezawa.bj/merci?x=1", "http://localhost:3002/merci"], _clock);

        Assert.True(app.AllowsReturnUrl("https://chezawa.bj/merci"));
        Assert.False(app.AllowsReturnUrl("https://chezawa.bj/autre"));
        DomainAssert.Throws(SalesAppErrors.ReturnUrlInvalid, () => app.SetReturnUrls(["http://chezawa.bj/merci"], _clock));
        DomainAssert.Throws(SalesAppErrors.ReturnUrlInvalid, () => app.SetReturnUrls(["javascript:alert(1)"], _clock));
    }

    [Fact]
    public void A_rotated_key_replaces_the_old_one_and_an_app_can_be_disabled_then_enabled()
    {
        var app = Web();

        app.RotateKey(Hash('c'), "wxyz", _clock);
        var same = Assert.Throws<DomainException>(() => app.RotateKey(Hash('c'), "wxyz", _clock));
        app.Disable(_clock);
        var twice = Assert.Throws<DomainException>(() => app.Disable(_clock));
        app.Enable(_clock);

        Assert.Equal(Hash('c'), app.KeyHash);
        Assert.Equal("wxyz", app.KeyHint);
        Assert.Equal(SalesAppErrors.KeyUnchanged, same.Error);
        Assert.Equal(SalesAppErrors.AlreadyDisabled, twice.Error);
        Assert.True(app.IsActive);
        Assert.DoesNotContain("aaaa", app.KeyHash.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Names_hints_and_hashes_are_validated()
    {
        DomainAssert.Throws(SalesAppErrors.NameRequired, () => SalesApp.Create(Tenant, " ", SalesAppKind.Web, Hash('a'), "abcd", _clock));
        DomainAssert.Throws(SalesAppErrors.KindInvalid, () => SalesApp.Create(Tenant, "X", (SalesAppKind)9, Hash('a'), "abcd", _clock));
        DomainAssert.Throws(SalesAppErrors.KeyHintInvalid, () => SalesApp.Create(Tenant, "X", SalesAppKind.Web, Hash('a'), "ab", _clock));
        DomainAssert.Throws(SalesAppErrors.KeyHashInvalid, () => PublishableKeyHash.Create("ABC"));
    }
}

public sealed class OnlineOrderingSettingsTests
{
    private readonly FakeClock _clock = new();

    private OnlineOrderingSettings Default() =>
        OnlineOrderingSettings.Default(new LocationReference(TenantId.New(), LocationId.New()), _clock);

    [Fact]
    public void Defaults_offer_takeaway_paid_online_or_at_the_counter_and_accepted_automatically()
    {
        var settings = Default();

        Assert.Equal([ServiceMode.Takeaway], settings.ServiceModes);
        Assert.Equal(OnlineOrderingSettings.DefaultPreparationMinutes, settings.PreparationMinutes);
        Assert.All(Enum.GetValues<SalesAppKind>(), channel =>
            Assert.Equal(ChannelPolicy.Create(channel, true, true, null, OrderAcceptance.Automatic), settings.PolicyFor(channel)));
        Assert.False(settings.IsPaused);
    }

    [Fact]
    public void Configure_replaces_everything_and_keeps_the_rules_of_omitted_channels()
    {
        var settings = Default();
        var kiosk = ChannelPolicy.Create(SalesAppKind.Kiosk, false, true, 15000m, OrderAcceptance.Manual);

        settings.Configure(true, [ServiceMode.Delivery, ServiceMode.Takeaway, ServiceMode.Takeaway], 30, 2000m, 5, 20, [kiosk], _clock);

        Assert.True(settings.IsPaused);
        Assert.Equal([ServiceMode.Takeaway, ServiceMode.Delivery], settings.ServiceModes);
        Assert.Equal((30, (decimal?)2000m, 5, 20), (settings.PreparationMinutes, settings.MinimumOrderAmount, settings.ManualAcceptanceMinutes, settings.UnpaidExpiryMinutes));
        Assert.Equal(kiosk, settings.PolicyFor(SalesAppKind.Kiosk));
        Assert.Equal(ChannelPolicy.Default(SalesAppKind.Web), settings.PolicyFor(SalesAppKind.Web));
        Assert.True(settings.Offers(ServiceMode.Delivery));
        Assert.False(settings.Offers(ServiceMode.DineIn));
    }

    [Fact]
    public void Invalid_settings_are_refused()
    {
        var settings = Default();

        DomainAssert.Throws(OnlineOrderingErrors.ServiceModeRequired, () => settings.Configure(false, [], 20, null, 10, 15, [], _clock));
        DomainAssert.Throws(OnlineOrderingErrors.PreparationInvalid, () => settings.Configure(false, [ServiceMode.Takeaway], 1, null, 10, 15, [], _clock));
        DomainAssert.Throws(OnlineOrderingErrors.AcceptanceDelayInvalid, () => settings.Configure(false, [ServiceMode.Takeaway], 20, null, 90, 15, [], _clock));
        DomainAssert.Throws(OnlineOrderingErrors.ExpiryInvalid, () => settings.Configure(false, [ServiceMode.Takeaway], 20, null, 10, 2, [], _clock));
        DomainAssert.Throws(OnlineOrderingErrors.AmountInvalid, () => settings.Configure(false, [ServiceMode.Takeaway], 20, -1m, 10, 15, [], _clock));
        DomainAssert.Throws(OnlineOrderingErrors.PaymentRequired, () => ChannelPolicy.Create(SalesAppKind.Web, false, false, null, OrderAcceptance.Automatic));
        DomainAssert.Throws(OnlineOrderingErrors.CounterLimitWithoutCounter, () => ChannelPolicy.Create(SalesAppKind.Web, true, false, 10m, OrderAcceptance.Automatic));
        DomainAssert.Throws(OnlineOrderingErrors.PolicyDuplicated, () => settings.Configure(
            false, [ServiceMode.Takeaway], 20, null, 10, 15, [ChannelPolicy.Default(SalesAppKind.Web), ChannelPolicy.Default(SalesAppKind.Web)], _clock));
    }

    [Fact]
    public void Pausing_touches_nothing_else()
    {
        var settings = Default();

        settings.SetPaused(true, _clock);
        settings.SetPaused(true, _clock);

        Assert.True(settings.IsPaused);
        Assert.Single(settings.DomainEvents);
    }
}

public sealed class OnlineOrderTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private SalesApp App() =>
        SalesApp.Create(Tenant, "Site", SalesAppKind.Web, PublishableKeyHash.Create(new string('a', PublishableKeyHash.Length)), "abcd", _clock);

    private OnlineOrder Record(OrderAcceptance acceptance, PaymentMode payment = PaymentMode.Counter) =>
        OnlineOrder.Record(Tenant, OrderId.New(), App(), LocationId.New(), payment, acceptance, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), _clock);

    [Fact]
    public void An_automatic_order_is_handled_at_once_a_manual_one_waits_until_its_deadline()
    {
        var automatic = Record(OrderAcceptance.Automatic);
        var manual = Record(OrderAcceptance.Manual);

        Assert.Equal((OnlineOrderState.Handled, (DateTimeOffset?)null), (automatic.State, automatic.AcceptBeforeUtc));
        Assert.True(manual.IsAwaitingAcceptance);
        Assert.Equal(_clock.UtcNow.AddMinutes(10), manual.AcceptBeforeUtc);
        DomainAssert.Throws(OnlineOrderErrors.NotOverdue, () => manual.Expire(_clock));
        _clock.Advance(TimeSpan.FromMinutes(10));
        manual.Expire(_clock);
        Assert.Equal(OnlineOrderState.Expired, manual.State);
        DomainAssert.Throws(OnlineOrderErrors.NotCancellable, () => manual.CancelByCustomer(_clock));
    }

    [Fact]
    public void Only_a_waiting_order_can_be_cancelled_by_the_customer_and_handling_is_idempotent()
    {
        var manual = Record(OrderAcceptance.Manual);
        var automatic = Record(OrderAcceptance.Automatic);

        manual.CancelByCustomer(_clock);
        manual.MarkHandled(_clock);

        Assert.Equal(OnlineOrderState.CancelledByCustomer, manual.State);
        DomainAssert.Throws(OnlineOrderErrors.NotCancellable, () => automatic.CancelByCustomer(_clock));
        Assert.ThrowsAny<Exception>(() => OnlineOrder.Record(
            TenantId.New(), OrderId.New(), App(), LocationId.New(), PaymentMode.Counter, OrderAcceptance.Automatic, TimeSpan.Zero, TimeSpan.Zero, _clock));
        DomainAssert.Throws(OnlineOrderErrors.PaymentModeInvalid, () => OnlineOrder.Record(
            Tenant, OrderId.New(), App(), LocationId.New(), (PaymentMode)7, OrderAcceptance.Automatic, TimeSpan.Zero, TimeSpan.Zero, _clock));
    }

    [Fact]
    public void An_order_paid_online_waits_for_its_payment_then_follows_its_acceptance_from_the_payment_on()
    {
        var automatic = Record(OrderAcceptance.Automatic, PaymentMode.Online);
        var manual = Record(OrderAcceptance.Manual, PaymentMode.Online);
        var unpaid = Record(OrderAcceptance.Automatic, PaymentMode.Online);

        Assert.True(automatic.IsAwaitingPayment);
        Assert.Equal(_clock.UtcNow.AddMinutes(15), automatic.PayBeforeUtc);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(automatic.MarkPaid(_clock));
        Assert.False(manual.MarkPaid(_clock));
        Assert.False(manual.MarkPaid(_clock));
        _clock.Advance(TimeSpan.FromMinutes(11));
        unpaid.Expire(_clock);

        Assert.Equal(OnlineOrderState.Handled, automatic.State);
        Assert.True(automatic.IsPaidOnline);
        Assert.Equal(OnlineOrderState.AwaitingAcceptance, manual.State);
        Assert.Equal(FakeClock.DefaultNow.AddMinutes(15), manual.AcceptBeforeUtc);
        Assert.True(manual.IsOverdue(_clock.UtcNow));
        Assert.Equal(OnlineOrderState.Expired, unpaid.State);
        Assert.Null(unpaid.NextDeadlineUtc);
        DomainAssert.Throws(OnlineOrderingErrors.ExpiryInvalid, () => OnlineOrder.Record(
            Tenant, OrderId.New(), App(), LocationId.New(), PaymentMode.Online, OrderAcceptance.Automatic, TimeSpan.Zero, TimeSpan.Zero, _clock));
    }

    [Fact]
    public void A_kiosk_never_requires_the_anti_robot_check()
    {
        var clock = new FakeClock();
        var kiosk = SalesApp.Create(TenantId.New(), "Borne", SalesAppKind.Kiosk, PublishableKeyHash.Create(new string('a', 64)), "abcd", clock);
        var web = SalesApp.Create(TenantId.New(), "Site", SalesAppKind.Web, PublishableKeyHash.Create(new string('b', 64)), "abcd", clock);

        var refused = Assert.Throws<DomainException>(() => kiosk.SetHumanCheck(true, clock));
        web.SetHumanCheck(true, clock);

        Assert.Equal(SalesAppErrors.HumanCheckNotForKiosk, refused.Error);
        Assert.True(web.RequiresHumanCheck);
        Assert.False(kiosk.RequiresHumanCheck);
    }
}

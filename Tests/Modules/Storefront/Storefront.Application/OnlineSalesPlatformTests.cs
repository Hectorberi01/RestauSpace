using Storefront.Application.Abstractions;
using Storefront.Application.Platform;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class OnlineSalesPlatformTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);
    private readonly FakeClock _clock = new();
    private readonly FakePlatform _platform = new();
    private readonly TenantId _busy = TenantId.New();
    private readonly TenantId _quiet = TenantId.New();

    public OnlineSalesPlatformTests()
    {
        var created = FakeClock.DefaultNow.AddDays(-30);
        _platform.Apps.Add(new(_busy, SalesAppId.New(), "Site", SalesAppKind.Web, SalesAppStatus.Active, created));
        _platform.Apps.Add(new(_busy, SalesAppId.New(), "Bornes", SalesAppKind.Kiosk, SalesAppStatus.Active, created));
        _platform.Apps.Add(new(_quiet, SalesAppId.New(), "Ancienne appli", SalesAppKind.MobileApp, SalesAppStatus.Disabled, created));
        var location = LocationId.New();
        _platform.Kiosks.Add(new(_busy, KioskDeviceId.New(), _platform.Apps[1].Id, location, "Entrée", KioskDeviceStatus.Active, created, null));
        _platform.Kiosks.Add(new(_busy, KioskDeviceId.New(), _platform.Apps[1].Id, location, "Caisse", KioskDeviceStatus.Revoked, created, created.AddDays(2)));
        _platform.Settings.Add(new(_busy, location, false, [ServiceMode.Takeaway, ServiceMode.Delivery], 4, created));
        _platform.Settings.Add(new(_quiet, LocationId.New(), true, [ServiceMode.Takeaway], null, created));
        _platform.Orders.Add(new(_busy, SalesAppKind.Web, PaymentMode.Online, OnlineOrderState.Handled, 7));
        _platform.Orders.Add(new(_busy, SalesAppKind.Web, PaymentMode.Counter, OnlineOrderState.Expired, 2));
        _platform.Orders.Add(new(_busy, SalesAppKind.Kiosk, PaymentMode.Counter, OnlineOrderState.Handled, 5));
        _platform.Last.Add(new(_busy, created.AddDays(20)));
        _platform.Last.Add(new(_quiet, created.AddDays(-90)));
        _platform.Sessions.Add(new(_busy, 3, 2));
    }

    [Fact]
    public async Task Overview_counts_apps_kiosks_locations_orders_and_customers()
    {
        var overview = await new GetOnlineSalesOverviewHandler(_platform, _clock).HandleAsync(new(From, To));

        Assert.Equal(1, overview.TenantsWithApps);
        Assert.Equal(1, overview.TenantsWithOrders);
        Assert.Equal((1, 0), (overview.Apps.Single(a => a.Kind == SalesAppKind.Web).Active, overview.Apps.Single(a => a.Kind == SalesAppKind.Web).Disabled));
        Assert.Equal(1, overview.Apps.Single(a => a.Kind == SalesAppKind.MobileApp).Disabled);
        Assert.Equal((1, 1), (overview.ActiveKiosks, overview.RevokedKiosks));
        Assert.Equal((2, 1), (overview.ConfiguredLocations, overview.PausedLocations));
        var web = overview.Channels.Single(c => c.Channel == SalesAppKind.Web);
        Assert.Equal((9, 7, 2, 7), (web.Placed, web.Handled, web.Expired, web.PaidOnline));
        Assert.Equal(5, overview.Channels.Single(c => c.Channel == SalesAppKind.Kiosk).Placed);
        Assert.Equal(0, overview.Channels.Single(c => c.Channel == SalesAppKind.MobileApp).Placed);
        Assert.Equal((3, 2), (overview.ActiveCustomerSessions, overview.CustomerSignIns));
        Assert.Equal((new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)), _platform.LastPeriod);
    }

    [Fact]
    public async Task Tenants_are_listed_most_active_first_with_their_counts()
    {
        var rows = await new ListOnlineSalesTenantsHandler(_platform, _clock).HandleAsync(new(From, To));

        Assert.Equal([_busy.Value, _quiet.Value], rows.Select(r => r.TenantId));
        var busy = rows[0];
        Assert.Equal((1, 0, 1, 0), (busy.WebApps, busy.MobileApps, busy.KioskApps, busy.DisabledApps));
        Assert.Equal((1, 1, 0), (busy.ActiveKiosks, busy.ConfiguredLocations, busy.PausedLocations));
        Assert.Equal((14, 2, 3), (busy.OrdersPlaced, busy.OrdersExpired, busy.ActiveCustomerSessions));
        Assert.Equal(FakeClock.DefaultNow.AddDays(-10), busy.LastOrderAtUtc);
        var quiet = rows[1];
        Assert.Equal((0, 1, 1, 0), (quiet.MobileApps, quiet.DisabledApps, quiet.PausedLocations, quiet.OrdersPlaced));

        Assert.Equal([_quiet.Value], (await new ListOnlineSalesTenantsHandler(_platform, _clock).HandleAsync(new(From, To, 1, 1))).Select(r => r.TenantId));
    }

    [Fact]
    public async Task Tenant_detail_reads_only_that_tenant()
    {
        var detail = await new GetOnlineSalesTenantHandler(_platform, _clock).HandleAsync(new(_busy.Value, From, To));

        Assert.Equal(_busy, _platform.LastTenant);
        Assert.Equal(["Site", "Bornes"], detail.Apps.Select(a => a.Name));
        Assert.Equal(["Entrée", "Caisse"], detail.Kiosks.Select(k => k.Name));
        Assert.Equal([ServiceMode.Takeaway, ServiceMode.Delivery], detail.Locations.Single().ServiceModes);
        Assert.Equal(14, detail.Channels.Sum(c => c.Placed));
        Assert.Equal((3, 2), (detail.ActiveCustomerSessions, detail.CustomerSignIns));
    }

    [Theory]
    [InlineData("2026-02-01", "2026-01-31")]
    [InlineData("2025-01-01", "2026-01-02")]
    public async Task Period_must_be_ordered_and_at_most_a_year(string from, string to)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            new GetOnlineSalesOverviewHandler(_platform, _clock).HandleAsync(new(DateOnly.Parse(from), DateOnly.Parse(to))));
        Assert.Equal(OnlineSalesPlatformErrors.PeriodInvalid.Code, exception.Code);
    }

    [Fact]
    public async Task Tenant_detail_requires_a_tenant()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            new GetOnlineSalesTenantHandler(_platform, _clock).HandleAsync(new(Guid.Empty, From, To)));
        Assert.Equal(OnlineSalesPlatformErrors.TenantRequired.Code, exception.Code);
    }

    private sealed class FakePlatform : IOnlineSalesPlatform
    {
        public List<PlatformSalesApp> Apps { get; } = [];

        public List<PlatformKiosk> Kiosks { get; } = [];

        public List<PlatformOrderCount> Orders { get; } = [];

        public List<PlatformLastOrder> Last { get; } = [];

        public List<PlatformLocationSettings> Settings { get; } = [];

        public List<PlatformCustomerSessions> Sessions { get; } = [];

        public (DateTimeOffset, DateTimeOffset) LastPeriod { get; private set; }

        public TenantId? LastTenant { get; private set; }

        private IReadOnlyList<T> Of<T>(IEnumerable<T> rows, Func<T, TenantId> tenant, TenantId? wanted)
        {
            LastTenant = wanted;
            return rows.Where(row => wanted is null || tenant(row) == wanted).ToList();
        }

        public Task<IReadOnlyList<PlatformSalesApp>> AppsAsync(TenantId? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(Apps, row => row.TenantId, tenantId));

        public Task<IReadOnlyList<PlatformKiosk>> KiosksAsync(TenantId? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(Kiosks, row => row.TenantId, tenantId));

        public Task<IReadOnlyList<PlatformOrderCount>> OrdersAsync(
            TenantId? tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        {
            LastPeriod = (fromUtc, toUtc);
            return Task.FromResult(Of(Orders, row => row.TenantId, tenantId));
        }

        public Task<IReadOnlyList<PlatformLastOrder>> LastOrdersAsync(TenantId? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(Last, row => row.TenantId, tenantId));

        public Task<IReadOnlyList<PlatformLocationSettings>> SettingsAsync(TenantId? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(Settings, row => row.TenantId, tenantId));

        public Task<IReadOnlyList<PlatformCustomerSessions>> CustomerSessionsAsync(
            TenantId? tenantId, DateTimeOffset nowUtc, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Of(Sessions, row => row.TenantId, tenantId));
    }
}

using Storefront.Application.Abstractions;
using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

public sealed class StorefrontUseCaseTests
{
    private readonly StorefrontFixture _fixture = new();

    private Task<IssuedSalesAppKey> WebsiteAsync(params Guid[] locations) =>
        _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand(
            "Site de Chez Awa", SalesAppKind.Web, ["https://chezawa.bj"], locations, ["https://chezawa.bj/merci"]));

    [Fact]
    public async Task A_restaurant_creates_a_website_whose_key_is_shown_once_and_kept_only_as_a_fingerprint()
    {
        var issued = await WebsiteAsync();
        var overview = await _fixture.Overview.HandleAsync(new GetStorefrontOverviewQuery());

        var app = Assert.Single(_fixture.Apps);
        Assert.StartsWith(PublishableKeys.Prefix, issued.Key, StringComparison.Ordinal);
        Assert.Equal(PublishableKeys.Hash(issued.Key), app.KeyHash);
        Assert.Equal(issued.Key[^4..], app.KeyHint);
        Assert.DoesNotContain(issued.Key, issued.ToString(), StringComparison.Ordinal);
        var dto = Assert.Single(overview.Apps);
        Assert.DoesNotContain(issued.Key, System.Text.Json.JsonSerializer.Serialize(overview), StringComparison.Ordinal);
        Assert.Equal((true, false, false), (overview.Access.Web, overview.Access.MobileApp, overview.Access.Kiosk));
        Assert.Equal(2, overview.Locations.Count);
        Assert.All(overview.Locations, location => Assert.False(location.IsSaved));
        Assert.Equal(["https://chezawa.bj"], dto.AllowedOrigins);
    }

    [Fact]
    public async Task A_channel_needs_its_option_and_locations_come_from_the_restaurant()
    {
        var kiosk = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.CreateApp.HandleAsync(new CreateSalesAppCommand("Borne", SalesAppKind.Kiosk)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => WebsiteAsync(_fixture.Paris.Value));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => WebsiteAsync(Guid.NewGuid()));

        Assert.Equal(StorefrontApplicationErrors.OptionRequired, kiosk.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, unknown.Error);
        Assert.Empty(_fixture.Apps);
    }

    [Fact]
    public async Task A_key_opens_its_own_restaurant_only_from_its_origins_and_never_once_rotated_or_disabled()
    {
        var issued = await WebsiteAsync();

        var server = await _fixture.SignInAsync(issued.Key);
        var browser = await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");
        var otherSite = await _fixture.SignInAsync(issued.Key, "https://evil.example");
        var tampered = await _fixture.SignInAsync(issued.Key + "x");
        var garbage = await _fixture.SignInAsync("not a key");
        _fixture.CurrentTenantId = _fixture.TenantA;
        var rotated = await _fixture.RotateKey.HandleAsync(new RotateSalesAppKeyCommand(issued.Id));
        var oldKey = await _fixture.SignInAsync(issued.Key);
        var newKey = await _fixture.SignInAsync(rotated.Key);
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.DisableApp.HandleAsync(new DisableSalesAppCommand(issued.Id));
        var disabled = await _fixture.SignInAsync(rotated.Key);

        Assert.Equal(_fixture.TenantA.Value, server!.TenantId);
        Assert.NotNull(browser);
        Assert.Null(otherSite);
        Assert.Null(tampered);
        Assert.Null(garbage);
        Assert.Null(oldKey);
        Assert.NotNull(newKey);
        Assert.Null(disabled);
    }

    [Fact]
    public async Task A_suspended_restaurant_keys_are_refused_and_another_restaurant_never_reaches_the_app()
    {
        var issued = await WebsiteAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        var otherTenant = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RotateKey.HandleAsync(new RotateSalesAppKeyCommand(issued.Id)));
        _fixture.Restaurants[_fixture.TenantA] = _fixture.Restaurants[_fixture.TenantA] with { AcceptsBusinessWrites = false };

        var suspended = await _fixture.SignInAsync(issued.Key);

        Assert.Equal(StorefrontApplicationErrors.AppNotFound, otherTenant.Error);
        Assert.Null(suspended);
    }

    [Fact]
    public async Task The_app_reads_its_locations_with_opening_and_the_rules_of_its_channel()
    {
        var issued = await WebsiteAsync(_fixture.Cotonou.Value, _fixture.Calavi.Value);
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(
            _fixture.Cotonou.Value, false, [ServiceMode.Takeaway, ServiceMode.Delivery], 25, 3000m, 10, 15,
            [new ChannelPolicyInput(SalesAppKind.Web, true, false, null, OrderAcceptance.Automatic)]));
        var zone = new StorefrontDeliveryZone(Guid.NewGuid(), "Cadjèhoun", 1000m, 5000m, 20);
        _fixture.DeliveryOffers[_fixture.Cotonou] = new StorefrontDeliveryOffer(30, [zone]);
        await _fixture.SignInAsync(issued.Key, "https://chezawa.bj");

        var storefront = await _fixture.Storefront.HandleAsync(new GetStorefrontQuery());
        var cotonou = await _fixture.StorefrontLocation.HandleAsync(new GetStorefrontLocationQuery(_fixture.Cotonou.Value));
        var paris = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.StorefrontLocation.HandleAsync(new GetStorefrontLocationQuery(_fixture.Paris.Value)));

        Assert.Equal(("Chez Awa", "XOF"), (storefront.Restaurant.Name, storefront.Restaurant.CurrencyCode));
        Assert.True(storefront.OrderingEnabled);
        Assert.Equal(["Calavi", "Cotonou"], storefront.Locations.Select(location => location.Name));
        Assert.True(cotonou.AcceptsOrdersNow);
        Assert.Equal([ServiceMode.Takeaway, ServiceMode.Delivery], cotonou.ServiceModes);
        Assert.Equal(new StorefrontPaymentDto(true, false, null), cotonou.Payment);
        Assert.Equal(30, cotonou.Delivery!.LeadMinutes);
        Assert.Equal(new StorefrontDeliveryZoneDto(zone.ZoneId, "Cadjèhoun", 1000m, 5000m, 20), Assert.Single(cotonou.Delivery.Zones));
        Assert.Equal((25, (decimal?)3000m), (cotonou.PreparationMinutes, cotonou.MinimumOrderAmount));
        var calavi = storefront.Locations.Single(location => location.Name == "Calavi");
        Assert.False(calavi.AcceptsOrdersNow);
        Assert.Null(calavi.Delivery);
        Assert.NotNull(calavi.NextOpeningUtc);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, paris.Error);
    }

    [Fact]
    public async Task Without_the_option_or_while_paused_the_app_still_reads_but_takes_no_order()
    {
        var issued = await WebsiteAsync();
        await _fixture.Pause.HandleAsync(new PauseOnlineOrderingCommand(_fixture.Cotonou.Value, true));
        await _fixture.SignInAsync(issued.Key);

        var paused = await _fixture.StorefrontLocation.HandleAsync(new GetStorefrontLocationQuery(_fixture.Cotonou.Value));
        _fixture.Options.Clear();
        var withoutOption = await _fixture.Storefront.HandleAsync(new GetStorefrontQuery());

        Assert.True(paused.IsPaused);
        Assert.False(paused.AcceptsOrdersNow);
        Assert.False(withoutOption.OrderingEnabled);
        Assert.All(withoutOption.Locations, location => Assert.False(location.AcceptsOrdersNow));
    }

    [Fact]
    public async Task Disabling_is_always_possible_enabling_needs_the_option_again()
    {
        var issued = await WebsiteAsync();
        _fixture.Restaurants[_fixture.TenantA] = _fixture.Restaurants[_fixture.TenantA] with { AcceptsBusinessWrites = false };
        await _fixture.DisableApp.HandleAsync(new DisableSalesAppCommand(issued.Id));
        var notWritable = await Assert.ThrowsAsync<DomainException>(() => _fixture.EnableApp.HandleAsync(new EnableSalesAppCommand(issued.Id)));
        _fixture.Restaurants[_fixture.TenantA] = _fixture.Restaurants[_fixture.TenantA] with { AcceptsBusinessWrites = true };
        _fixture.Options.Clear();
        var noOption = await Assert.ThrowsAsync<DomainException>(() => _fixture.EnableApp.HandleAsync(new EnableSalesAppCommand(issued.Id)));

        Assert.Equal(StorefrontApplicationErrors.TenantNotWritable, notWritable.Error);
        Assert.Equal(StorefrontApplicationErrors.OptionRequired, noOption.Error);
        Assert.False(Assert.Single(_fixture.Apps).IsActive);
    }

    [Fact]
    public async Task Settings_are_created_once_per_location_then_updated()
    {
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(_fixture.Cotonou.Value, false, [ServiceMode.DineIn], 15, null, 10, 15, null));
        await _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(_fixture.Cotonou.Value, true, [ServiceMode.Takeaway], 40, null, 10, 15, null));
        var invalid = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(_fixture.Cotonou.Value, false, [], 15, null, 10, 15, null)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.SaveSettings.HandleAsync(new SaveOnlineOrderingSettingsCommand(_fixture.Paris.Value, false, [ServiceMode.DineIn], 15, null, 10, 15, null)));
        var overview = await _fixture.Overview.HandleAsync(new GetStorefrontOverviewQuery());

        var settings = Assert.Single(_fixture.Settings);
        Assert.Equal((true, 40), (settings.IsPaused, settings.PreparationMinutes));
        Assert.Equal(OnlineOrderingErrors.ServiceModeRequired, invalid.Error);
        Assert.Equal(StorefrontApplicationErrors.LocationNotFound, foreign.Error);
        Assert.True(overview.Locations.Single(location => location.LocationId == _fixture.Cotonou.Value).IsSaved);
    }
}

namespace Delivery.Domain;

public class DeliverySettingsTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationReference Place = new(Tenant, LocationId.New(), IsOperational: true);

    private readonly FakeClock _clock = new();

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private DeliverySettings NewSettings()
    {
        var settings = DeliverySettings.Create(Tenant, Place, Currency.XOF, _clock);
        settings.ClearDomainEvents();
        return settings;
    }

    [Fact]
    public void New_settings_offer_pickup_only()
    {
        var settings = DeliverySettings.Create(Tenant, Place, Currency.XOF, _clock);

        Assert.True(settings.PickupEnabled);
        Assert.Equal(DeliverySettings.DefaultPickupLeadMinutes, settings.PickupLeadMinutes);
        Assert.False(settings.DeliveryEnabled);
        Assert.Equal(DeliverySettings.DefaultDeliveryLeadMinutes, settings.DeliveryLeadMinutes);
        Assert.Empty(settings.Zones);
        Assert.Equal(Place.LocationId, DomainAssert.Raised<DeliverySettingsCreated>(settings).LocationId);
    }

    [Fact]
    public void Settings_belong_to_the_tenant_of_their_location()
    {
        var elsewhere = new LocationReference(TenantId.New(), LocationId.New(), IsOperational: true);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => DeliverySettings.Create(Tenant, elsewhere, Currency.XOF, _clock));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(241)]
    public void Lead_times_stay_between_0_and_240_minutes(int minutes)
    {
        var settings = NewSettings();

        DomainAssert.Throws(DeliverySettingsErrors.LeadTimeOutOfRange, () => settings.ConfigurePickup(true, minutes, _clock));
        DomainAssert.Throws(DeliverySettingsErrors.LeadTimeOutOfRange, () => settings.ConfigureDelivery(false, minutes, _clock));
    }

    [Fact]
    public void Delivery_needs_an_active_zone()
    {
        var settings = NewSettings();

        DomainAssert.Throws(DeliverySettingsErrors.DeliveryNeedsZone, () => settings.ConfigureDelivery(true, 30, _clock));

        settings.AddZone("Centre", Xof(1000), Xof(5000), 20, _clock);
        settings.ConfigureDelivery(true, 30, _clock);

        Assert.True(settings.DeliveryEnabled);
        Assert.Equal(30, settings.DeliveryLeadMinutes);
        Assert.True(DomainAssert.Raised<DeliveryServicesChanged>(settings).DeliveryEnabled);
    }

    [Fact]
    public void A_zone_is_validated_and_its_name_is_unique()
    {
        var settings = NewSettings();
        settings.AddZone("  Centre  ", Xof(1000), Xof(0), 20, _clock);

        Assert.Equal("Centre", Assert.Single(settings.Zones).Name);
        DomainAssert.Throws(DeliverySettingsErrors.ZoneNameRequired, () => settings.AddZone(" ", Xof(0), Xof(0), 20, _clock));
        DomainAssert.Throws(DeliverySettingsErrors.ZoneNameAlreadyUsed, () => settings.AddZone("centre", Xof(0), Xof(0), 20, _clock));
        DomainAssert.Throws(DeliverySettingsErrors.ZoneAmountNegative, () => settings.AddZone("Nord", Xof(-1), Xof(0), 20, _clock));
        DomainAssert.Throws(DeliverySettingsErrors.ZoneMinutesOutOfRange, () => settings.AddZone("Nord", Xof(0), Xof(0), 4, _clock));
        DomainAssert.Throws(CommonErrors.CurrencyMismatch, () => settings.AddZone("Nord", Money.Of(1, Currency.EUR), Xof(0), 20, _clock));
    }

    [Fact]
    public void A_zone_can_be_changed_deactivated_and_removed()
    {
        var settings = NewSettings();
        var zone = settings.AddZone("Centre", Xof(1000), Xof(5000), 20, _clock);

        settings.UpdateZone(zone, "Plateau", Xof(1500), Xof(7000), 30, _clock);
        settings.SetZoneActive(zone, false, _clock);

        var changed = Assert.Single(settings.Zones);
        Assert.Equal(("Plateau", 1500m, 7000m, 30, false), (changed.Name, changed.Fee.Amount, changed.MinimumOrder.Amount, changed.EstimatedMinutes, changed.IsActive));
        Assert.Null(settings.ActiveZone(zone));

        settings.RemoveZone(zone, _clock);
        Assert.Empty(settings.Zones);
        DomainAssert.Throws(DeliverySettingsErrors.ZoneNotFound, () => settings.RemoveZone(zone, _clock));
    }

    [Fact]
    public void The_last_active_zone_stays_while_delivery_is_on()
    {
        var settings = NewSettings();
        var zone = settings.AddZone("Centre", Xof(1000), Xof(0), 20, _clock);
        settings.ConfigureDelivery(true, 30, _clock);

        DomainAssert.Throws(DeliverySettingsErrors.LastActiveZone, () => settings.SetZoneActive(zone, false, _clock));
        DomainAssert.Throws(DeliverySettingsErrors.LastActiveZone, () => settings.RemoveZone(zone, _clock));

        settings.ConfigureDelivery(false, 30, _clock);
        settings.RemoveZone(zone, _clock);
        Assert.Empty(settings.Zones);
    }
}

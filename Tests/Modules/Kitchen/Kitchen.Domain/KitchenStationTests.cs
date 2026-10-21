namespace Kitchen.Domain;

public class KitchenStationTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly LocationReference LocationRef = KitchenRefs.Location(Tenant, Location);
    private readonly FakeClock _clock = new();

    private KitchenStation NewStation()
    {
        var station = KitchenStation.Create(Tenant, LocationRef, StationName.Create("Grill"), isNameUniqueInLocation: true, _clock);
        station.ClearDomainEvents();
        return station;
    }

    [Fact]
    public void StationName_create_trims_and_validates()
    {
        Assert.Equal("Grill", StationName.Create("  Grill ").Value);
        Assert.Equal(StationName.Create("Grill"), StationName.Create("Grill"));
        Assert.Equal(StationName.MaxLength, StationName.Create(new string('a', StationName.MaxLength)).Value.Length);
        DomainAssert.Throws(KitchenStationErrors.NameTooLong, () => StationName.Create(new string('a', StationName.MaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StationName_create_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(KitchenStationErrors.NameEmpty, () => StationName.Create(value));

    [Fact]
    public void Create_builds_an_active_station_and_raises_KitchenStationCreated()
    {
        var station = KitchenStation.Create(Tenant, LocationRef, StationName.Create("Grill"), isNameUniqueInLocation: true, _clock);

        Assert.Equal(Tenant, station.TenantId);
        Assert.Equal(Location, station.LocationId);
        Assert.Equal("Grill", station.Name.Value);
        Assert.True(station.IsActive);

        var created = DomainAssert.Raised<KitchenStationCreated>(station);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(station.Id, created.StationId);
        Assert.Equal(Location, created.LocationId);
        Assert.Equal("Grill", created.Name);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_rejects_a_name_already_used_in_the_location() =>
        DomainAssert.Throws(
            KitchenStationErrors.NameNotUnique,
            () => KitchenStation.Create(Tenant, LocationRef, StationName.Create("Grill"), isNameUniqueInLocation: false, _clock));

    [Fact]
    public void Create_rejects_empty_tenant_or_location()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => KitchenStation.Create(default, LocationRef, StationName.Create("Grill"), true, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Location, true));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant_and_accepts_one_that_is_not_operational()
    {
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => KitchenStation.Create(Tenant, KitchenRefs.Location(TenantId.New(), Location), StationName.Create("Grill"), true, _clock));

        var station = KitchenStation.Create(
            Tenant, new LocationReference(Tenant, Location, IsOperational: false), StationName.Create("Grill"), true, _clock);

        Assert.Equal(Location, station.LocationId);
    }

    [Fact]
    public void Rename_changes_the_name_when_unique()
    {
        var station = NewStation();

        station.Rename(StationName.Create("Cold"), isNameUniqueInLocation: true);

        Assert.Equal("Cold", station.Name.Value);
    }

    [Fact]
    public void Rename_rejects_a_name_already_used_in_the_location()
    {
        var station = NewStation();

        DomainAssert.Throws(
            KitchenStationErrors.NameNotUnique,
            () => station.Rename(StationName.Create("Cold"), isNameUniqueInLocation: false));

        Assert.Equal("Grill", station.Name.Value);
    }

    [Fact]
    public void Rename_to_the_same_name_is_a_no_op_whatever_the_uniqueness_answer()
    {
        var station = NewStation();
        var name = station.Name;

        station.Rename(StationName.Create("Grill"), isNameUniqueInLocation: false);

        Assert.Same(name, station.Name);
        DomainAssert.NoEvents(station);
    }

    [Fact]
    public void Deactivate_turns_the_station_off_and_raises_KitchenStationDeactivated()
    {
        var station = NewStation();

        station.Deactivate(_clock);

        Assert.False(station.IsActive);
        Assert.False(station.ToReference().IsActive);
        var deactivated = DomainAssert.Raised<KitchenStationDeactivated>(station);
        Assert.Equal(station.Id, deactivated.StationId);
        Assert.Equal(Tenant, deactivated.TenantId);
        Assert.Equal(Location, deactivated.LocationId);
    }

    [Fact]
    public void Deactivate_rejects_an_already_inactive_station()
    {
        var station = NewStation();
        station.Deactivate(_clock);
        station.ClearDomainEvents();

        DomainAssert.Throws(KitchenStationErrors.AlreadyInactive, () => station.Deactivate(_clock));

        DomainAssert.NoEvents(station);
    }

    [Fact]
    public void Activate_turns_an_inactive_station_back_on_and_raises_KitchenStationActivated()
    {
        var station = NewStation();
        station.Deactivate(_clock);
        station.ClearDomainEvents();

        station.Activate(_clock);

        Assert.True(station.IsActive);
        var activated = DomainAssert.Raised<KitchenStationActivated>(station);
        Assert.Equal(station.Id, activated.StationId);
        Assert.Equal(Tenant, activated.TenantId);
    }

    [Fact]
    public void Activate_rejects_an_already_active_station()
    {
        var station = NewStation();

        DomainAssert.Throws(KitchenStationErrors.AlreadyActive, () => station.Activate(_clock));

        Assert.True(station.IsActive);
        DomainAssert.NoEvents(station);
    }

    [Fact]
    public void ToReference_carries_id_tenant_location_and_activity()
    {
        var station = NewStation();

        var reference = station.ToReference();

        Assert.Equal(station.Id, reference.Id);
        Assert.Equal(Tenant, reference.TenantId);
        Assert.Equal(Location, reference.LocationId);
        Assert.True(reference.IsActive);

        station.Deactivate(_clock);
        Assert.False(station.ToReference().IsActive);
    }

    [Fact]
    public void Module_ids_reject_an_empty_guid_and_round_trip()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => StationId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenTicketId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => KitchenTicketItemId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => StationRoutingId.From(Guid.Empty));

        var id = StationId.New();
        Assert.Equal(id, StationId.From(id.Value));
    }
}

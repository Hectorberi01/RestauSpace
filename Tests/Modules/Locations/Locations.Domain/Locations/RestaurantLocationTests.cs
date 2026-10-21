using Locations.Domain.Locations;

namespace Locations.Domain.Locations;

public sealed class RestaurantLocationTests
{
    // Le 2026-03-02 est un lundi.
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Friday = Monday.AddDays(4);
    private static readonly DateOnly Saturday = Monday.AddDays(5);

    // UTC+1 toute l'année, sans heure d'été.
    private static readonly TimeZoneId PortoNovo = TimeZoneId.Create("Africa/Porto-Novo");
    private static readonly TimeZoneId Paris = TimeZoneId.Create("Europe/Paris");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    /// <summary>Instant UTC d'une date et heure locales de Porto-Novo.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
        new(date.ToDateTime(T(hour, minute)), TimeSpan.FromHours(1));

    private static Period Slot(DateOnly date, int hour, int minute, DateOnly endDate, int endHour, int endMinute = 0) =>
        Period.Create(At(date, hour, minute), At(endDate, endHour, endMinute));

    private RestaurantLocation ActiveLocationIn(TimeZoneId zone, BusinessHours hours)
    {
        var location = RestaurantLocation.Create(
            _tenantId, LocationName.Create("Cotonou Centre"), AddressIn(), hours, Capacity.Create(40), zone, _clock);
        location.Activate(_clock);
        location.ClearDomainEvents();
        return location;
    }

    private static Address AddressIn(string city = "Cotonou") => Address.Create("12 rue des Cheminots", city, null, "BJ");

    /// <summary>Du lundi au vendredi 12:00-14:00, plus le vendredi 22:00-02:00 sur la nuit.</summary>
    private static BusinessHours WeekHours() =>
        BusinessHours.Create(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(day => new WeeklySlot(day, TimeRange.SameDay(T(12), T(14))))
                .Append(new WeeklySlot(DayOfWeek.Friday, TimeRange.Overnight(T(22), T(2)))));

    private RestaurantLocation DraftLocation()
    {
        var location = RestaurantLocation.Create(
            _tenantId, LocationName.Create("Cotonou Centre"), AddressIn(), WeekHours(), Capacity.Create(40), PortoNovo, _clock);
        location.ClearDomainEvents();
        return location;
    }

    private RestaurantLocation ActiveLocation()
    {
        var location = DraftLocation();
        location.Activate(_clock);
        location.ClearDomainEvents();
        return location;
    }

    private RestaurantLocation TemporarilyClosedLocation()
    {
        var location = ActiveLocation();
        location.TemporarilyClose(_clock, "Travaux");
        location.ClearDomainEvents();
        return location;
    }

    private RestaurantLocation ArchivedLocation()
    {
        var location = ActiveLocation();
        location.Archive(_clock);
        location.ClearDomainEvents();
        return location;
    }

    // ---- Create ----

    [Fact]
    public void Create_starts_a_draft_location_owned_by_the_tenant()
    {
        var location = RestaurantLocation.Create(
            _tenantId, LocationName.Create("Cotonou Centre"), AddressIn(), WeekHours(), Capacity.Create(40), PortoNovo, _clock);

        Assert.NotEqual(Guid.Empty, location.Id.Value);
        Assert.Equal(_tenantId, location.TenantId);
        Assert.Equal("Cotonou Centre", location.Name.Value);
        Assert.Equal(AddressIn(), location.Address);
        Assert.Equal(WeekHours(), location.Hours);
        Assert.Equal(40, location.Capacity.Seats);
        Assert.Equal(PortoNovo, location.TimeZone);
        Assert.Equal(LocationStatus.Draft, location.Status);
        Assert.Equal(_clock.UtcNow, location.CreatedAtUtc);
        Assert.Empty(location.ExceptionalSchedules);
        Assert.False(location.IsOperational);
    }

    [Fact]
    public void Create_raises_RestaurantLocationCreated()
    {
        var location = RestaurantLocation.Create(
            _tenantId, LocationName.Create("Cotonou Centre"), AddressIn(), WeekHours(), Capacity.Create(40), PortoNovo, _clock);

        var created = DomainAssert.Raised<RestaurantLocationCreated>(location);
        Assert.Equal(_tenantId, created.TenantId);
        Assert.Equal(location.Id, created.LocationId);
        Assert.Equal("Cotonou Centre", created.Name);
        Assert.Equal("BJ", created.CountryCode);
        Assert.Equal(40, created.Capacity);
        Assert.Equal("Africa/Porto-Novo", created.TimeZoneId);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => RestaurantLocation.Create(
                default, LocationName.Create("Cotonou Centre"), AddressIn(), WeekHours(), Capacity.Zero, PortoNovo, _clock));

    [Fact]
    public void Create_cannot_be_reached_with_a_negative_capacity_or_inconsistent_hours()
    {
        DomainAssert.Throws(
            LocationErrors.CapacityNegative,
            () => RestaurantLocation.Create(
                _tenantId, LocationName.Create("Cotonou Centre"), AddressIn(), WeekHours(), Capacity.Create(-1), PortoNovo, _clock));
        DomainAssert.Throws(
            LocationErrors.RangeStartNotBeforeEnd,
            () => RestaurantLocation.Create(
                _tenantId,
                LocationName.Create("Cotonou Centre"),
                AddressIn(),
                BusinessHours.Create(new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(T(14), T(12)))),
                Capacity.Zero,
                PortoNovo,
                _clock));
    }

    [Fact]
    public void Create_rejects_null_arguments()
    {
        var name = LocationName.Create("Cotonou Centre");

        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, null!, AddressIn(), WeekHours(), Capacity.Zero, PortoNovo, _clock));
        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, name, null!, WeekHours(), Capacity.Zero, PortoNovo, _clock));
        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, name, AddressIn(), null!, Capacity.Zero, PortoNovo, _clock));
        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, name, AddressIn(), WeekHours(), null!, PortoNovo, _clock));
        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, name, AddressIn(), WeekHours(), Capacity.Zero, null!, _clock));
        Assert.Throws<ArgumentNullException>(() => RestaurantLocation.Create(_tenantId, name, AddressIn(), WeekHours(), Capacity.Zero, PortoNovo, null!));
    }

    [Fact]
    public void Location_guards_cross_tenant_references()
    {
        var location = DraftLocation();

        location.EnsureSameTenant(_tenantId);
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => location.EnsureSameTenant(TenantId.New()));
    }

    // ---- Activate ----

    [Fact]
    public void Activate_moves_a_draft_location_to_active_and_raises_LocationActivated()
    {
        var location = DraftLocation();

        location.Activate(_clock);

        Assert.Equal(LocationStatus.Active, location.Status);
        Assert.True(location.IsOperational);
        var activated = DomainAssert.Raised<LocationActivated>(location);
        Assert.Equal(_tenantId, activated.TenantId);
        Assert.Equal(location.Id, activated.LocationId);
    }

    [Fact]
    public void Activate_rejects_an_active_location()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(LocationErrors.OnlyDraftCanBeActivated, () => location.Activate(_clock));
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void Activate_rejects_a_temporarily_closed_location()
    {
        var location = TemporarilyClosedLocation();

        DomainAssert.Throws(LocationErrors.OnlyDraftCanBeActivated, () => location.Activate(_clock));
        Assert.Equal(LocationStatus.TemporarilyClosed, location.Status);
    }

    // ---- TemporarilyClose / Reopen ----

    [Fact]
    public void TemporarilyClose_closes_an_active_location_and_raises_LocationTemporarilyClosed()
    {
        var location = ActiveLocation();

        location.TemporarilyClose(_clock, "  Travaux ");

        Assert.Equal(LocationStatus.TemporarilyClosed, location.Status);
        Assert.Equal("Travaux", location.TemporaryClosureReason);
        Assert.False(location.IsOperational);
        var closed = DomainAssert.Raised<LocationTemporarilyClosed>(location);
        Assert.Equal(_tenantId, closed.TenantId);
        Assert.Equal(location.Id, closed.LocationId);
        Assert.Equal("Travaux", closed.Reason);
    }

    [Fact]
    public void TemporarilyClose_accepts_a_missing_reason()
    {
        var location = ActiveLocation();

        location.TemporarilyClose(_clock);

        Assert.Null(location.TemporaryClosureReason);
        Assert.Null(DomainAssert.Raised<LocationTemporarilyClosed>(location).Reason);
    }

    [Fact]
    public void TemporarilyClose_rejects_a_reason_longer_than_the_maximum()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(
            LocationErrors.ReasonTooLong,
            () => location.TemporarilyClose(_clock, new string('a', RestaurantLocation.ReasonMaxLength + 1)));
        Assert.Equal(LocationStatus.Active, location.Status);
    }

    [Fact]
    public void TemporarilyClose_rejects_a_draft_location()
    {
        var location = DraftLocation();

        DomainAssert.Throws(LocationErrors.OnlyActiveCanBeTemporarilyClosed, () => location.TemporarilyClose(_clock));
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void TemporarilyClose_rejects_an_already_closed_location()
    {
        var location = TemporarilyClosedLocation();

        DomainAssert.Throws(LocationErrors.OnlyActiveCanBeTemporarilyClosed, () => location.TemporarilyClose(_clock, "Autre"));
        Assert.Equal("Travaux", location.TemporaryClosureReason);
    }

    [Fact]
    public void Temporarily_closed_location_is_never_open_whatever_its_hours()
    {
        var location = TemporarilyClosedLocation();

        Assert.False(location.IsOpenAt(Monday, T(12, 30)));
    }

    [Fact]
    public void Reopen_brings_a_temporarily_closed_location_back_to_active()
    {
        var location = TemporarilyClosedLocation();

        location.Reopen(_clock);

        Assert.Equal(LocationStatus.Active, location.Status);
        Assert.Null(location.TemporaryClosureReason);
        Assert.True(location.IsOpenAt(Monday, T(12, 30)));
        Assert.Equal(location.Id, DomainAssert.Raised<LocationReopened>(location).LocationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reopen_rejects_a_location_that_is_not_temporarily_closed(bool active)
    {
        var location = active ? ActiveLocation() : DraftLocation();

        DomainAssert.Throws(LocationErrors.OnlyTemporarilyClosedCanBeReopened, () => location.Reopen(_clock));
        DomainAssert.NoEvents(location);
    }

    // ---- Archive (terminal) ----

    [Fact]
    public void Archive_can_be_reached_from_draft_active_and_temporarily_closed()
    {
        var cases = new (RestaurantLocation Location, LocationStatus Previous)[]
        {
            (DraftLocation(), LocationStatus.Draft),
            (ActiveLocation(), LocationStatus.Active),
            (TemporarilyClosedLocation(), LocationStatus.TemporarilyClosed),
        };

        foreach (var (location, previous) in cases)
        {
            location.Archive(_clock);

            Assert.Equal(LocationStatus.Archived, location.Status);
            Assert.Equal(_clock.UtcNow, location.ArchivedAtUtc);
            Assert.Null(location.TemporaryClosureReason);
            var archived = DomainAssert.Raised<LocationArchived>(location);
            Assert.Equal(_tenantId, archived.TenantId);
            Assert.Equal(previous, archived.PreviousStatus);
        }
    }

    [Fact]
    public void Archived_is_terminal_and_cannot_be_reactivated()
    {
        var location = ArchivedLocation();

        DomainAssert.Throws(LocationErrors.Archived, () => location.Activate(_clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.Reopen(_clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.TemporarilyClose(_clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.Archive(_clock));
        Assert.Equal(LocationStatus.Archived, location.Status);
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void Archived_is_terminal_and_rejects_every_change()
    {
        var location = ArchivedLocation();

        DomainAssert.Throws(LocationErrors.Archived, () => location.Rename(LocationName.Create("Autre"), _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.ChangeAddress(AddressIn("Porto-Novo"), _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.SetBusinessHours(BusinessHours.AlwaysClosed, _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.SetCapacity(Capacity.Create(10), _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.SetCapacity(location.Capacity, _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.ChangeTimeZone(Paris, _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.ChangeTimeZone(location.TimeZone, _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.AddExceptionalClosure(Monday, _clock));
        DomainAssert.Throws(
            LocationErrors.Archived,
            () => location.AddExceptionalSchedule(Monday, [TimeRange.SameDay(T(10), T(12))], _clock));
        DomainAssert.Throws(LocationErrors.Archived, () => location.RemoveExceptionalSchedule(Monday, _clock));
        Assert.Equal("Cotonou Centre", location.Name.Value);
        Assert.Equal(40, location.Capacity.Seats);
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void Archived_location_is_never_open()
    {
        var location = ArchivedLocation();

        Assert.False(location.IsOperational);
        Assert.False(location.IsOpenAt(Monday, T(12, 30)));
    }

    // ---- Paramètres ----

    [Fact]
    public void Rename_changes_the_name_and_raises_LocationRenamed()
    {
        var location = ActiveLocation();

        location.Rename(LocationName.Create("Cotonou Plage"), _clock);

        Assert.Equal("Cotonou Plage", location.Name.Value);
        var renamed = DomainAssert.Raised<LocationRenamed>(location);
        Assert.Equal("Cotonou Centre", renamed.PreviousName);
        Assert.Equal("Cotonou Plage", renamed.NewName);
    }

    [Fact]
    public void ChangeAddress_replaces_the_address_and_raises_LocationAddressChanged()
    {
        var location = ActiveLocation();

        location.ChangeAddress(AddressIn("Porto-Novo"), _clock);

        Assert.Equal("Porto-Novo", location.Address.City);
        var changed = DomainAssert.Raised<LocationAddressChanged>(location);
        Assert.Equal(_tenantId, changed.TenantId);
        Assert.Equal(AddressIn("Porto-Novo"), changed.Address);
    }

    [Fact]
    public void SetBusinessHours_replaces_the_hours_and_raises_BusinessHoursChanged()
    {
        var location = ActiveLocation();
        var newHours = BusinessHours.Create(new WeeklySlot(DayOfWeek.Sunday, TimeRange.SameDay(T(10), T(16))));

        location.SetBusinessHours(newHours, _clock);

        Assert.Equal(newHours, location.Hours);
        Assert.False(location.IsOpenAt(Monday, T(12, 30)));
        Assert.True(location.IsOpenAt(Monday.AddDays(6), T(12, 30)));
        var changed = DomainAssert.Raised<BusinessHoursChanged>(location);
        Assert.Equal(_tenantId, changed.TenantId);
        Assert.Equal(location.Id, changed.LocationId);
        Assert.Equal(newHours, changed.Hours);
    }

    [Fact]
    public void SetBusinessHours_cannot_be_reached_with_overlapping_ranges()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => location.SetBusinessHours(
                BusinessHours.Create(
                    new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(T(12), T(15))),
                    new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(T(14), T(18)))),
                _clock));
        Assert.Equal(WeekHours(), location.Hours);
        DomainAssert.NotRaised<BusinessHoursChanged>(location);
    }

    [Fact]
    public void SetCapacity_changes_the_capacity_and_raises_LocationCapacityChanged()
    {
        var location = ActiveLocation();

        location.SetCapacity(Capacity.Zero, _clock);

        Assert.Equal(0, location.Capacity.Seats);
        var changed = DomainAssert.Raised<LocationCapacityChanged>(location);
        Assert.Equal(40, changed.PreviousCapacity);
        Assert.Equal(0, changed.NewCapacity);
    }

    [Fact]
    public void SetCapacity_cannot_be_reached_with_a_negative_capacity()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(LocationErrors.CapacityNegative, () => location.SetCapacity(Capacity.Create(-5), _clock));
        Assert.Equal(40, location.Capacity.Seats);
    }

    [Fact]
    public void Settings_changes_to_the_same_value_raise_nothing()
    {
        var location = ActiveLocation();

        location.Rename(LocationName.Create("Cotonou Centre"), _clock);
        location.ChangeAddress(AddressIn(), _clock);
        location.SetBusinessHours(WeekHours(), _clock);
        location.SetCapacity(Capacity.Create(40), _clock);
        location.ChangeTimeZone(TimeZoneId.Create("Africa/Porto-Novo"), _clock);

        DomainAssert.NoEvents(location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_can_be_changed_while_draft_or_temporarily_closed(bool temporarilyClosed)
    {
        var location = temporarilyClosed ? TemporarilyClosedLocation() : DraftLocation();

        location.Rename(LocationName.Create("Cotonou Plage"), _clock);
        location.ChangeAddress(AddressIn("Porto-Novo"), _clock);
        location.SetBusinessHours(BusinessHours.AlwaysClosed, _clock);
        location.SetCapacity(Capacity.Create(12), _clock);
        location.AddExceptionalClosure(Monday, _clock);

        Assert.Equal(5, location.DomainEvents.Count);
    }

    // ---- IsOpenAt (horaires hebdomadaires) ----

    [Fact]
    public void IsOpenAt_follows_the_weekly_hours_of_an_active_location()
    {
        var location = ActiveLocation();

        Assert.True(location.IsOpenAt(Monday, T(12)));
        Assert.True(location.IsOpenAt(Monday, T(13, 59)));
        Assert.False(location.IsOpenAt(Monday, T(14)));
        Assert.False(location.IsOpenAt(Monday, T(20)));
        Assert.False(location.IsOpenAt(Saturday, T(12, 30)));
    }

    [Fact]
    public void IsOpenAt_weekly_overnight_range_stays_open_after_midnight()
    {
        var location = ActiveLocation();

        Assert.True(location.IsOpenAt(Friday, T(23)));
        Assert.True(location.IsOpenAt(Saturday, T(1, 59)));
        Assert.False(location.IsOpenAt(Saturday, T(2)));
        Assert.False(location.IsOpenAt(Friday, T(1)));
    }

    [Fact]
    public void IsOpenAt_is_false_for_a_draft_location() =>
        Assert.False(DraftLocation().IsOpenAt(Monday, T(12, 30)));

    // ---- Horaires exceptionnels ----

    [Fact]
    public void AddExceptionalClosure_closes_that_date_only()
    {
        var location = ActiveLocation();

        var schedule = location.AddExceptionalClosure(Monday, _clock, " Jour férié ");

        Assert.Same(schedule, Assert.Single(location.ExceptionalSchedules));
        Assert.Equal(Monday, schedule.Date);
        Assert.True(schedule.IsClosed);
        Assert.Empty(schedule.Ranges);
        Assert.Equal("Jour férié", schedule.Reason);
        Assert.False(location.IsOpenAt(Monday, T(12, 30)));
        Assert.True(location.IsOpenAt(Tuesday, T(12, 30)));
        Assert.True(location.IsOpenAt(Monday.AddDays(7), T(12, 30)));
    }

    [Fact]
    public void AddExceptionalClosure_raises_ExceptionalScheduleAdded()
    {
        var location = ActiveLocation();

        var schedule = location.AddExceptionalClosure(Monday, _clock);

        var added = DomainAssert.Raised<ExceptionalScheduleAdded>(location);
        Assert.Equal(_tenantId, added.TenantId);
        Assert.Equal(location.Id, added.LocationId);
        Assert.Equal(schedule.Id, added.ExceptionalScheduleId);
        Assert.Equal(Monday, added.Date);
        Assert.True(added.IsClosed);
    }

    [Fact]
    public void AddExceptionalSchedule_replaces_the_weekly_ranges_of_that_date()
    {
        var location = ActiveLocation();

        var schedule = location.AddExceptionalSchedule(
            Monday, [TimeRange.SameDay(T(18), T(22)), TimeRange.SameDay(T(9), T(11))], _clock, "Soirée spéciale");

        Assert.False(schedule.IsClosed);
        Assert.Equal(
            new[] { TimeRange.SameDay(T(9), T(11)), TimeRange.SameDay(T(18), T(22)) },
            schedule.Ranges.ToArray());
        Assert.True(location.IsOpenAt(Monday, T(10)));
        Assert.False(location.IsOpenAt(Monday, T(12, 30)));
        Assert.True(location.IsOpenAt(Monday, T(19)));
        Assert.False(DomainAssert.Raised<ExceptionalScheduleAdded>(location).IsClosed);
    }

    [Fact]
    public void AddExceptionalSchedule_can_open_a_day_that_is_normally_closed()
    {
        var location = ActiveLocation();

        location.AddExceptionalSchedule(Saturday, [TimeRange.SameDay(T(12), T(15))], _clock);

        Assert.True(location.IsOpenAt(Saturday, T(13)));
        Assert.False(location.IsOpenAt(Saturday.AddDays(7), T(13)));
    }

    [Fact]
    public void AddExceptionalSchedule_rejects_a_second_exception_on_the_same_date()
    {
        var location = ActiveLocation();
        location.AddExceptionalClosure(Monday, _clock);
        location.ClearDomainEvents();

        DomainAssert.Throws(LocationErrors.ExceptionalDateAlreadyDefined, () => location.AddExceptionalClosure(Monday, _clock));
        DomainAssert.Throws(
            LocationErrors.ExceptionalDateAlreadyDefined,
            () => location.AddExceptionalSchedule(Monday, [TimeRange.SameDay(T(10), T(12))], _clock));
        Assert.Single(location.ExceptionalSchedules);
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void AddExceptionalSchedule_accepts_the_same_weekday_on_another_date()
    {
        var location = ActiveLocation();
        location.AddExceptionalClosure(Monday, _clock);

        location.AddExceptionalClosure(Monday.AddDays(7), _clock);

        Assert.Equal(2, location.ExceptionalSchedules.Count);
    }

    [Fact]
    public void AddExceptionalSchedule_rejects_an_opening_without_range()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(
            LocationErrors.ExceptionalOpeningWithoutRange,
            () => location.AddExceptionalSchedule(Monday, [], _clock));
        Assert.Empty(location.ExceptionalSchedules);
    }

    [Fact]
    public void AddExceptionalSchedule_treats_a_null_range_as_a_programming_error()
    {
        var location = ActiveLocation();

        Assert.Throws<ArgumentNullException>(
            () => location.AddExceptionalSchedule(Monday, [TimeRange.SameDay(new TimeOnly(12, 0), new TimeOnly(14, 0)), null!], _clock));
        Assert.Empty(location.ExceptionalSchedules);
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void AddExceptionalSchedule_rejects_overlapping_ranges()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => location.AddExceptionalSchedule(
                Monday, [TimeRange.SameDay(T(10), T(14)), TimeRange.SameDay(T(13), T(16))], _clock));
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => location.AddExceptionalSchedule(
                Monday, [TimeRange.Overnight(T(18), T(2)), TimeRange.SameDay(T(23), T(23, 30))], _clock));
        Assert.Empty(location.ExceptionalSchedules);
        DomainAssert.NoEvents(location);
    }

    [Fact]
    public void AddExceptionalSchedule_rejects_a_reason_longer_than_the_maximum()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(
            LocationErrors.ReasonTooLong,
            () => location.AddExceptionalClosure(Monday, _clock, new string('a', ExceptionalSchedule.ReasonMaxLength + 1)));
        Assert.Empty(location.ExceptionalSchedules);
    }

    [Fact]
    public void Exceptional_closure_cancels_the_overnight_service_starting_that_date()
    {
        var location = ActiveLocation();

        location.AddExceptionalClosure(Friday, _clock);

        Assert.False(location.IsOpenAt(Friday, T(23)));
        Assert.False(location.IsOpenAt(Saturday, T(1)));
    }

    [Fact]
    public void Exceptional_closure_does_not_cancel_the_tail_of_the_previous_overnight_service()
    {
        var location = ActiveLocation();

        location.AddExceptionalClosure(Saturday, _clock);

        Assert.True(location.IsOpenAt(Friday, T(23)));
        Assert.True(location.IsOpenAt(Saturday, T(1)));
        Assert.False(location.IsOpenAt(Saturday, T(2)));
    }

    [Fact]
    public void Exceptional_overnight_opening_stays_open_after_midnight_on_the_next_date()
    {
        var location = ActiveLocation();

        location.AddExceptionalSchedule(Monday, [TimeRange.Overnight(T(20), T(3))], _clock);

        Assert.True(location.IsOpenAt(Monday, T(21)));
        Assert.True(location.IsOpenAt(Tuesday, T(2, 59)));
        Assert.False(location.IsOpenAt(Tuesday, T(3)));
        Assert.True(location.IsOpenAt(Tuesday, T(12, 30)));
    }

    [Fact]
    public void RangesInForceOn_returns_the_exception_ranges_or_the_weekly_ones()
    {
        var location = ActiveLocation();
        location.AddExceptionalClosure(Monday, _clock);

        Assert.Empty(location.RangesInForceOn(Monday));
        Assert.Equal(new[] { TimeRange.SameDay(T(12), T(14)) }, location.RangesInForceOn(Tuesday).ToArray());
    }

    [Fact]
    public void RemoveExceptionalSchedule_restores_the_weekly_hours_and_raises_an_event()
    {
        var location = ActiveLocation();
        var schedule = location.AddExceptionalClosure(Monday, _clock);
        location.ClearDomainEvents();

        location.RemoveExceptionalSchedule(Monday, _clock);

        Assert.Empty(location.ExceptionalSchedules);
        Assert.True(location.IsOpenAt(Monday, T(12, 30)));
        var removed = DomainAssert.Raised<ExceptionalScheduleRemoved>(location);
        Assert.Equal(schedule.Id, removed.ExceptionalScheduleId);
        Assert.Equal(Monday, removed.Date);
    }

    [Fact]
    public void RemoveExceptionalSchedule_then_add_allows_redefining_the_date()
    {
        var location = ActiveLocation();
        location.AddExceptionalClosure(Monday, _clock);
        location.RemoveExceptionalSchedule(Monday, _clock);

        location.AddExceptionalSchedule(Monday, [TimeRange.SameDay(T(8), T(10))], _clock);

        Assert.False(Assert.Single(location.ExceptionalSchedules).IsClosed);
    }

    [Fact]
    public void RemoveExceptionalSchedule_rejects_a_date_without_exception()
    {
        var location = ActiveLocation();

        DomainAssert.Throws(LocationErrors.ExceptionalScheduleNotFound, () => location.RemoveExceptionalSchedule(Monday, _clock));
        DomainAssert.NoEvents(location);
    }

    // ---- Fuseau horaire ----

    [Fact]
    public void ChangeTimeZone_changes_the_zone_and_raises_LocationTimeZoneChanged()
    {
        var location = ActiveLocation();

        location.ChangeTimeZone(Paris, _clock);

        Assert.Equal(Paris, location.TimeZone);
        var changed = DomainAssert.Raised<LocationTimeZoneChanged>(location);
        Assert.Equal(_tenantId, changed.TenantId);
        Assert.Equal(location.Id, changed.LocationId);
        Assert.Equal("Africa/Porto-Novo", changed.PreviousTimeZoneId);
        Assert.Equal("Europe/Paris", changed.NewTimeZoneId);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void ChangeTimeZone_is_allowed_while_draft_or_temporarily_closed()
    {
        var draft = DraftLocation();
        var closed = TemporarilyClosedLocation();

        draft.ChangeTimeZone(Paris, _clock);
        closed.ChangeTimeZone(Paris, _clock);

        Assert.Equal(Paris, draft.TimeZone);
        Assert.Equal(Paris, closed.TimeZone);
    }

    [Fact]
    public void ChangeTimeZone_moves_the_instants_at_which_the_same_wall_clock_hours_apply()
    {
        var location = ActiveLocation();
        var noonUtc = new DateTimeOffset(Monday.ToDateTime(T(12)), TimeSpan.Zero);

        // 12:00 UTC correspond à 13:00 à Porto-Novo (ouvert 12:00-14:00) et à 21:00 à Tokyo (fermé).
        Assert.True(location.IsOpenAtInstant(noonUtc));
        location.ChangeTimeZone(TimeZoneId.Create("Asia/Tokyo"), _clock);
        Assert.False(location.IsOpenAtInstant(noonUtc));
    }

    // ---- IsOpenAtInstant ----

    [Fact]
    public void IsOpenAtInstant_reads_the_instant_on_the_location_wall_clock()
    {
        var location = ActiveLocation();

        // Lundi 12:00-14:00 à Porto-Novo correspond à 11:00-13:00 UTC.
        Assert.False(location.IsOpenAtInstant(new DateTimeOffset(2026, 3, 2, 10, 59, 0, TimeSpan.Zero)));
        Assert.True(location.IsOpenAtInstant(new DateTimeOffset(2026, 3, 2, 11, 0, 0, TimeSpan.Zero)));
        Assert.True(location.IsOpenAtInstant(new DateTimeOffset(2026, 3, 2, 12, 59, 0, TimeSpan.Zero)));
        Assert.False(location.IsOpenAtInstant(new DateTimeOffset(2026, 3, 2, 13, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void IsOpenAtInstant_accepts_an_instant_given_with_any_offset()
    {
        var location = ActiveLocation();

        Assert.True(location.IsOpenAtInstant(new DateTimeOffset(2026, 3, 2, 7, 30, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void IsOpenAtInstant_follows_an_overnight_range_across_the_local_midnight()
    {
        var location = ActiveLocation();

        // Vendredi 22:00-02:00 en local : samedi 00:30 local est encore vendredi 23:30 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 6, 23, 30, 0, TimeSpan.Zero), At(Saturday, 0, 30).ToUniversalTime());
        Assert.True(location.IsOpenAtInstant(At(Saturday, 0, 30)));
        Assert.False(location.IsOpenAtInstant(At(Saturday, 2)));
    }

    [Fact]
    public void IsOpenAtInstant_is_false_unless_the_location_is_active()
    {
        Assert.False(DraftLocation().IsOpenAtInstant(At(Monday, 12, 30)));
        Assert.False(TemporarilyClosedLocation().IsOpenAtInstant(At(Monday, 12, 30)));
        Assert.False(ArchivedLocation().IsOpenAtInstant(At(Monday, 12, 30)));
    }

    // ---- IsOpenDuring ----

    [Fact]
    public void IsOpenDuring_accepts_a_slot_matching_the_opening_range_exactly()
    {
        var location = ActiveLocation();

        Assert.True(location.IsOpenDuring(Slot(Monday, 12, 0, Monday, 14)));
        Assert.True(location.IsOpenDuring(Slot(Monday, 12, 30, Monday, 13, 30)));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_slot_starting_before_the_opening_time()
    {
        var location = ActiveLocation();

        Assert.False(location.IsOpenDuring(Slot(Monday, 11, 59, Monday, 13)));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_slot_overflowing_the_closing_time()
    {
        var location = ActiveLocation();

        Assert.False(location.IsOpenDuring(Slot(Monday, 13, 0, Monday, 14, 1)));
        Assert.False(location.IsOpenDuring(Slot(Monday, 13, 0, Monday, 15)));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_slot_outside_the_opening_hours()
    {
        var location = ActiveLocation();

        Assert.False(location.IsOpenDuring(Slot(Monday, 16, 0, Monday, 18)));
        Assert.False(location.IsOpenDuring(Slot(Saturday, 12, 0, Saturday, 13)));
    }

    [Fact]
    public void IsOpenDuring_accepts_a_slot_inside_an_overnight_range_crossing_midnight()
    {
        var location = ActiveLocation();

        Assert.True(location.IsOpenDuring(Slot(Friday, 23, 0, Saturday, 1)));
        Assert.True(location.IsOpenDuring(Slot(Friday, 22, 0, Saturday, 2)));
        Assert.True(location.IsOpenDuring(Slot(Saturday, 0, 30, Saturday, 1, 30)));
        Assert.False(location.IsOpenDuring(Slot(Friday, 23, 0, Saturday, 2, 1)));
        Assert.False(location.IsOpenDuring(Slot(Friday, 21, 59, Saturday, 1)));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_slot_spanning_a_closed_gap_between_two_ranges()
    {
        var location = ActiveLocation();

        // Vendredi 12:00-14:00 puis 22:00-02:00 : ouvert aux deux extrémités, fermé entre les deux.
        Assert.False(location.IsOpenDuring(Slot(Friday, 13, 0, Friday, 23)));
    }

    [Fact]
    public void IsOpenDuring_chains_touching_ranges_including_across_midnight()
    {
        var hours = BusinessHours.Create(
            new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(T(12), T(14))),
            new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(T(14), T(18))),
            new WeeklySlot(DayOfWeek.Monday, TimeRange.Overnight(T(18), T(0))),
            new WeeklySlot(DayOfWeek.Tuesday, TimeRange.SameDay(T(0), T(3))));
        var location = ActiveLocationIn(PortoNovo, hours);

        Assert.True(location.IsOpenDuring(Slot(Monday, 13, 0, Tuesday, 3)));
        Assert.False(location.IsOpenDuring(Slot(Monday, 13, 0, Tuesday, 3, 1)));
    }

    [Fact]
    public void IsOpenDuring_follows_round_the_clock_hours_over_several_days()
    {
        var hours = BusinessHours.Create(
            Enum.GetValues<DayOfWeek>().Select(day => new WeeklySlot(day, TimeRange.Overnight(T(0), T(0)))));
        var location = ActiveLocationIn(PortoNovo, hours);

        Assert.True(location.IsOpenDuring(Slot(Monday, 9, 0, Monday.AddDays(10), 17)));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_slot_on_an_exceptionally_closed_date()
    {
        var location = ActiveLocation();
        Assert.True(location.IsOpenDuring(Slot(Monday, 12, 0, Monday, 14)));

        location.AddExceptionalClosure(Monday, _clock);

        Assert.False(location.IsOpenDuring(Slot(Monday, 12, 0, Monday, 14)));
        Assert.True(location.IsOpenDuring(Slot(Tuesday, 12, 0, Tuesday, 14)));
    }

    [Fact]
    public void IsOpenDuring_exceptional_closure_also_cancels_the_part_of_its_overnight_service_after_midnight()
    {
        var location = ActiveLocation();

        location.AddExceptionalClosure(Friday, _clock);

        Assert.False(location.IsOpenDuring(Slot(Friday, 23, 0, Saturday, 1)));
        Assert.False(location.IsOpenDuring(Slot(Saturday, 0, 30, Saturday, 1)));
    }

    [Fact]
    public void IsOpenDuring_follows_an_exceptional_opening()
    {
        var location = ActiveLocation();

        location.AddExceptionalSchedule(Monday, [TimeRange.SameDay(T(18), T(23))], _clock);

        Assert.True(location.IsOpenDuring(Slot(Monday, 19, 0, Monday, 21)));
        Assert.False(location.IsOpenDuring(Slot(Monday, 12, 0, Monday, 14)));
    }

    [Fact]
    public void IsOpenDuring_is_false_unless_the_location_is_active()
    {
        var slot = Slot(Monday, 12, 0, Monday, 14);

        Assert.False(DraftLocation().IsOpenDuring(slot));
        Assert.False(TemporarilyClosedLocation().IsOpenDuring(slot));
        Assert.False(ArchivedLocation().IsOpenDuring(slot));
    }

    [Fact]
    public void IsOpenDuring_rejects_a_null_slot() =>
        Assert.Throws<ArgumentNullException>(() => ActiveLocation().IsOpenDuring(null!));

    [Fact]
    public void IsOpenDuring_keeps_following_an_overnight_range_when_clocks_go_back()
    {
        // Paris, dimanche 2026-10-25 : 03:00 (UTC+2) devient 02:00 (UTC+1), donc samedi 22:00-04:00 dure 7 heures réelles.
        var hours = BusinessHours.Create(new WeeklySlot(DayOfWeek.Saturday, TimeRange.Overnight(T(22), T(4))));
        var location = ActiveLocationIn(Paris, hours);
        var opening = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);
        var closing = new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero);

        Assert.True(location.IsOpenDuring(Period.Create(opening, closing)));
        Assert.True(location.IsOpenDuring(Period.Create(opening.AddHours(3.5), closing.AddMinutes(-30))));
        Assert.False(location.IsOpenDuring(Period.Create(opening, closing.AddMinutes(1))));
        Assert.False(location.IsOpenDuring(Period.Create(opening.AddMinutes(-1), closing)));
    }

    [Fact]
    public void IsOpenDuring_closes_at_the_first_reading_of_a_closing_time_repeated_when_clocks_go_back()
    {
        // Samedi 22:00-02:30 : 02:30 se lit deux fois le dimanche 2026-10-25 ; la plage finit à la première (00:30 UTC).
        var hours = BusinessHours.Create(new WeeklySlot(DayOfWeek.Saturday, TimeRange.Overnight(T(22), T(2, 30))));
        var location = ActiveLocationIn(Paris, hours);
        var opening = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);
        var firstHalfPastTwo = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero);

        Assert.True(location.IsOpenDuring(Period.Create(opening, firstHalfPastTwo)));
        Assert.False(location.IsOpenDuring(Period.Create(opening, firstHalfPastTwo.AddMinutes(1))));
        Assert.False(location.IsOpenAtInstant(firstHalfPastTwo.AddMinutes(10)));
        Assert.True(location.IsOpenAtInstant(firstHalfPastTwo.AddMinutes(45)));
    }

    [Fact]
    public void IsOpenDuring_keeps_following_an_overnight_range_when_clocks_go_forward()
    {
        // Paris, dimanche 2026-03-29 : 02:00 (UTC+1) devient 03:00 (UTC+2), donc samedi 22:00-04:00 dure 5 heures réelles.
        var hours = BusinessHours.Create(new WeeklySlot(DayOfWeek.Saturday, TimeRange.Overnight(T(22), T(4))));
        var location = ActiveLocationIn(Paris, hours);
        var opening = new DateTimeOffset(2026, 3, 28, 21, 0, 0, TimeSpan.Zero);
        var closing = new DateTimeOffset(2026, 3, 29, 2, 0, 0, TimeSpan.Zero);

        Assert.True(location.IsOpenDuring(Period.Create(opening, closing)));
        Assert.False(location.IsOpenDuring(Period.Create(opening, closing.AddMinutes(1))));
    }

    [Fact]
    public void IsOpenDuring_bridges_two_ranges_separated_only_by_the_hour_skipped_when_clocks_go_forward()
    {
        // Dimanche 2026-03-29 à Paris : 01:00-02:00 puis 03:00-05:00, et 02:00-03:00 n'existe pas cette nuit-là.
        var hours = BusinessHours.Create(
            new WeeklySlot(DayOfWeek.Sunday, TimeRange.SameDay(T(1), T(2))),
            new WeeklySlot(DayOfWeek.Sunday, TimeRange.SameDay(T(3), T(5))));
        var location = ActiveLocationIn(Paris, hours);
        var halfPastOne = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);
        var halfPastThree = new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero);

        Assert.True(location.IsOpenDuring(Period.Create(halfPastOne, halfPastThree)));

        // Une semaine plus tard, l'heure 02:00-03:00 existe de nouveau et est fermée.
        Assert.False(location.IsOpenDuring(Period.Create(halfPastOne.AddDays(7).AddHours(-1), halfPastThree.AddDays(7))));
    }

    // ---- Données de référence pour les autres modules ----

    [Theory]
    [InlineData(LocationStatus.Draft, false)]
    [InlineData(LocationStatus.Active, true)]
    [InlineData(LocationStatus.TemporarilyClosed, false)]
    [InlineData(LocationStatus.Archived, false)]
    public void IsOperational_is_true_for_an_active_location_only(LocationStatus status, bool operational)
    {
        var location = status switch
        {
            LocationStatus.Draft => DraftLocation(),
            LocationStatus.Active => ActiveLocation(),
            LocationStatus.TemporarilyClosed => TemporarilyClosedLocation(),
            _ => ArchivedLocation(),
        };

        Assert.Equal(status, location.Status);
        Assert.Equal(operational, location.IsOperational);
        Assert.Equal(_tenantId, location.TenantId);
        Assert.NotEqual(Guid.Empty, location.Id.Value);
    }
}

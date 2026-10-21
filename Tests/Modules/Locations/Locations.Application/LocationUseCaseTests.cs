using Locations.Application.Commands;
using Locations.Application.Queries;
using Locations.Domain.Locations;

namespace Locations.Application;

public sealed class LocationUseCaseTests
{
    private readonly LocationsFixture _fixture = new();

    [Fact]
    public async Task Create_stores_a_draft_location_of_the_current_tenant_in_the_tenant_time_zone_by_default()
    {
        var id = await _fixture.Create.HandleAsync(LocationsFixture.NewLocation());

        var location = _fixture.Stored(id);
        Assert.Equal(_fixture.TenantA, location.TenantId);
        Assert.Equal(LocationStatus.Draft, location.Status);
        Assert.Equal("Africa/Porto-Novo", location.TimeZone.Value);
        Assert.Equal("BJ", location.Address.CountryCode.Value);
        Assert.Null(location.Address.PostalCode);
        Assert.Equal(40, location.Capacity.Seats);
        Assert.True(Assert.Single(location.Hours.RangesOn(DayOfWeek.Friday)).EndsNextDay);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<RestaurantLocationCreated>(location);
    }

    [Fact]
    public async Task Create_keeps_an_explicit_time_zone()
    {
        var id = await _fixture.Create.HandleAsync(LocationsFixture.NewLocation(timeZoneId: "Europe/Paris"));

        Assert.Equal("Europe/Paris", _fixture.Stored(id).TimeZone.Value);
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_can_be_read_or_written()
    {
        var id = await _fixture.GivenLocationAsync();
        _fixture.CurrentTenantId = null;

        var create = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Create.HandleAsync(LocationsFixture.NewLocation()));
        var read = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetById.HandleAsync(new GetLocationByIdQuery(id)));
        var list = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.List.HandleAsync(new ListLocationsQuery()));

        Assert.Equal(TenancyErrors.TenantRequired, create.Error);
        Assert.Equal(TenancyErrors.TenantRequired, read.Error);
        Assert.Equal(TenancyErrors.TenantRequired, list.Error);
    }

    [Fact]
    public async Task A_location_of_another_tenant_is_not_found_neither_to_read_nor_to_change()
    {
        var ofB = await _fixture.GivenLocationAsync(_fixture.TenantB);

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetLocationByIdQuery(ofB)));
        var rename = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Rename.HandleAsync(new RenameLocationCommand(ofB, "Volée")));
        var archive = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Archive.HandleAsync(new ArchiveLocationCommand(ofB)));

        Assert.Equal(LocationsApplicationErrors.NotFound, read.Error);
        Assert.Equal(LocationsApplicationErrors.NotFound, rename.Error);
        Assert.Equal(LocationsApplicationErrors.NotFound, archive.Error);
        Assert.Equal("Cotonou Centre", _fixture.Stored(ofB).Name.Value);
        Assert.Equal(LocationStatus.Draft, _fixture.Stored(ofB).Status);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task The_plan_limits_the_live_locations_and_an_archived_one_frees_its_place()
    {
        _fixture.MaxLocations[_fixture.TenantA] = 2;
        var first = await _fixture.GivenLocationAsync(name: "Cotonou");
        await _fixture.GivenLocationAsync(name: "Porto-Novo");
        await _fixture.GivenLocationAsync(_fixture.TenantB, "Paris");

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(LocationsFixture.NewLocation("Abomey")));
        await _fixture.Archive.HandleAsync(new ArchiveLocationCommand(first));
        var id = await _fixture.Create.HandleAsync(LocationsFixture.NewLocation("Abomey"));

        Assert.Equal(LocationsApplicationErrors.PlanLimitReached, refused.Error);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task List_shows_only_the_locations_of_the_current_tenant_by_name_and_hides_archived_ones()
    {
        await _fixture.GivenLocationAsync(name: "Porto-Novo");
        var archived = await _fixture.GivenLocationAsync(name: "Abomey");
        await _fixture.GivenLocationAsync(name: "Cotonou");
        await _fixture.GivenLocationAsync(_fixture.TenantB, "Paris");
        await _fixture.Archive.HandleAsync(new ArchiveLocationCommand(archived));

        var page = await _fixture.List.HandleAsync(new ListLocationsQuery());
        var withArchived = await _fixture.List.HandleAsync(new ListLocationsQuery(IncludeArchived: true));
        var secondPage = await _fixture.List.HandleAsync(new ListLocationsQuery(Page: 2, PageSize: 1));

        Assert.Equal(new[] { "Cotonou", "Porto-Novo" }, page.Items.Select(item => item.Name));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { "Abomey", "Cotonou", "Porto-Novo" }, withArchived.Items.Select(item => item.Name));
        Assert.Equal("Porto-Novo", Assert.Single(secondPage.Items).Name);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(100_001, 20)]
    public async Task List_refuses_a_page_out_of_bounds(int page, int pageSize)
    {
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.List.HandleAsync(new ListLocationsQuery(page, pageSize)));

        Assert.Equal(LocationsApplicationErrors.PagingInvalid, refused.Error);
    }

    [Fact]
    public async Task A_suspended_or_unknown_tenant_can_read_its_locations_but_not_change_them()
    {
        var id = await _fixture.GivenLocationAsync();
        _fixture.Tenants[_fixture.TenantA] = _fixture.Tenants[_fixture.TenantA] with { AcceptsBusinessWrites = false };

        var rename = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Rename.HandleAsync(new RenameLocationCommand(id, "Nouveau nom")));
        var create = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(LocationsFixture.NewLocation()));
        var read = await _fixture.GetById.HandleAsync(new GetLocationByIdQuery(id));

        Assert.Equal(LocationsApplicationErrors.TenantNotWritable, rename.Error);
        Assert.Equal(LocationsApplicationErrors.TenantNotWritable, create.Error);
        Assert.Equal("Cotonou Centre", read.Name);

        _fixture.Tenants.Remove(_fixture.TenantA);
        await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(LocationsFixture.NewLocation()));
    }

    [Theory]
    [InlineData("18h00")]
    [InlineData("24:00")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Times_must_be_written_hh_mm(string? start)
    {
        var id = await _fixture.GivenLocationAsync();

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetBusinessHours.HandleAsync(
            new SetBusinessHoursCommand(id, [new WeeklySlotInput(DayOfWeek.Monday, start, "22:00")])));

        Assert.Equal(LocationsApplicationErrors.TimeInvalid, refused.Error);
    }

    [Fact]
    public async Task Business_rules_stay_in_the_domain_and_surface_with_their_own_code()
    {
        var id = await _fixture.GivenLocationAsync();

        var overlap = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetBusinessHours.HandleAsync(
            new SetBusinessHoursCommand(id,
            [
                new WeeklySlotInput(DayOfWeek.Monday, "11:00", "15:00"),
                new WeeklySlotInput(DayOfWeek.Monday, "14:00", "22:00"),
            ])));

        Assert.Equal(LocationErrors.HoursRangesOverlap, overlap.Error);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task Get_returns_hours_and_exceptions_as_local_hh_mm_values()
    {
        var id = await _fixture.GivenLocationAsync();
        var christmas = new DateOnly(2026, 12, 25);
        var newYear = new DateOnly(2026, 12, 31);
        await _fixture.Activate.HandleAsync(new ActivateLocationCommand(id));
        await _fixture.AddClosure.HandleAsync(new AddExceptionalClosureCommand(id, christmas, "Noël"));
        await _fixture.AddOpening.HandleAsync(new AddExceptionalOpeningCommand(
            id, newYear, [new TimeRangeInput("20:00", "04:00", EndsNextDay: true)]));

        var dto = await _fixture.GetById.HandleAsync(new GetLocationByIdQuery(id));

        Assert.Equal(LocationStatus.Active, dto.Status);
        Assert.True(dto.IsOperational);
        Assert.Equal(new WeeklySlotDto(DayOfWeek.Friday, "18:00", "02:00", true), Assert.Single(dto.WeeklyHours));
        Assert.Collection(
            dto.ExceptionalSchedules,
            closure =>
            {
                Assert.Equal(christmas, closure.Date);
                Assert.True(closure.IsClosed);
                Assert.Equal("Noël", closure.Reason);
            },
            opening => Assert.Equal(new TimeRangeDto("20:00", "04:00", true), Assert.Single(opening.Ranges)));
        Assert.Equal(3, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_unknown_or_empty_id_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetLocationByIdQuery(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetLocationByIdQuery(Guid.Empty)));
    }

    [Fact]
    public async Task Opening_hours_are_answered_in_the_location_time_zone_and_only_once_it_is_active()
    {
        var id = await _fixture.GivenLocationAsync();
        var handler = new IsLocationOpenDuringHandler(_fixture, _fixture);
        // Vendredi 2026-01-16, 19:00-21:00 à Porto-Novo (UTC+1) = 18:00-20:00 UTC ; ouvert le vendredi 18:00-02:00.
        var friday = new IsLocationOpenDuringQuery(
            id, new DateTimeOffset(2026, 1, 16, 18, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 16, 20, 0, 0, TimeSpan.Zero));
        var thursday = friday with { StartUtc = friday.StartUtc.AddDays(-1), EndUtc = friday.EndUtc.AddDays(-1) };

        var whileDraft = await handler.HandleAsync(friday);
        await _fixture.Activate.HandleAsync(new ActivateLocationCommand(id));

        Assert.False(whileDraft);
        Assert.True(await handler.HandleAsync(friday));
        Assert.False(await handler.HandleAsync(thursday));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(friday with { LocationId = Guid.NewGuid() }));
    }
}

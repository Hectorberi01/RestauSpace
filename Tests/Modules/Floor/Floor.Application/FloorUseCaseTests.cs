using Floor.Application.Commands;
using Floor.Application.Queries;
using Floor.Domain;

namespace Floor.Application;

public sealed class FloorUseCaseTests
{
    private readonly FloorFixture _fixture = new();

    [Fact]
    public async Task A_dining_area_is_created_for_a_location_of_the_current_tenant()
    {
        var location = _fixture.GivenLocation();

        var id = await _fixture.CreateArea.HandleAsync(new CreateDiningAreaCommand(location, "  Salle   principale "));

        var area = _fixture.Areas.Stored(id);
        Assert.Equal(_fixture.TenantA, area.TenantId);
        Assert.Equal(location, area.LocationId.Value);
        Assert.Equal("Salle principale", area.Name);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<DiningAreaCreated>(area);
    }

    [Fact]
    public async Task A_location_of_another_tenant_or_unknown_is_not_found()
    {
        var ofB = _fixture.GivenLocation(_fixture.TenantB);

        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateArea.HandleAsync(new CreateDiningAreaCommand(ofB, "Terrasse")));
        var empty = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateArea.HandleAsync(new CreateDiningAreaCommand(Guid.Empty, "Terrasse")));

        Assert.Equal(FloorApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(FloorApplicationErrors.LocationNotFound, empty.Error);
        Assert.Empty(_fixture.Areas.Items);
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_can_be_read_or_written()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        _fixture.CurrentTenantId = null;

        var read = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetArea.HandleAsync(new GetDiningAreaByIdQuery(area)));
        var list = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.ListAreas.HandleAsync(new ListDiningAreasQuery()));
        var open = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2)));

        Assert.Equal(TenancyErrors.TenantRequired, read.Error);
        Assert.Equal(TenancyErrors.TenantRequired, list.Error);
        Assert.Equal(TenancyErrors.TenantRequired, open.Error);
    }

    [Fact]
    public async Task The_floor_of_another_tenant_is_not_found_neither_to_read_nor_to_change()
    {
        var (area, table) = await _fixture.GivenTableAsync(_fixture.TenantB);

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetArea.HandleAsync(new GetDiningAreaByIdQuery(area)));
        var rename = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.RenameArea.HandleAsync(new RenameDiningAreaCommand(area, "Volée")));
        var open = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2)));

        Assert.Equal(FloorApplicationErrors.DiningAreaNotFound, read.Error);
        Assert.Equal(FloorApplicationErrors.DiningAreaNotFound, rename.Error);
        Assert.Equal(FloorApplicationErrors.TableNotFound, open.Error);
        Assert.Equal("Terrasse", _fixture.Areas.Stored(area).Name);
        Assert.Empty(_fixture.Sessions.Items);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_floor_but_cannot_seat_anyone()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        _fixture.WritableTenants.Remove(_fixture.TenantA);

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2)));
        var read = await _fixture.GetArea.HandleAsync(new GetDiningAreaByIdQuery(area));

        Assert.Equal(FloorApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal("T1", Assert.Single(read.Tables).Label);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task Seating_guests_opens_a_session_shown_on_the_floor_plan()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        var reservation = Guid.NewGuid();

        var id = await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 3, reservation));

        var session = _fixture.Sessions.Stored(id);
        Assert.Equal(TableId.From(table), session.TableId);
        Assert.Equal(_fixture.Areas.Stored(area).LocationId, session.LocationId);
        Assert.Equal(3, session.GuestCount);
        Assert.Equal(ReservationReference.From(reservation), session.Reservation);
        var plan = Assert.Single(await _fixture.ListAreas.HandleAsync(new ListDiningAreasQuery()));
        Assert.Equal(id, Assert.Single(plan.Tables).OpenSessionId);
        Assert.Equal(id, Assert.Single(await _fixture.ListOpenSessions.HandleAsync(new ListOpenTableSessionsQuery())).Id);
    }

    [Fact]
    public async Task A_table_cannot_be_seated_twice_nor_while_out_of_service()
    {
        var (_, table) = await _fixture.GivenTableAsync();
        await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2));
        var (otherArea, otherTable) = await _fixture.GivenTableAsync();
        await _fixture.SetOutOfService.HandleAsync(new SetTableOutOfServiceCommand(otherArea, otherTable));

        var occupied = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2)));
        var outOfService = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(otherTable, 2)));

        Assert.Equal(TableSessionErrors.TableAlreadyOccupied, occupied.Error);
        Assert.Equal(TableSessionErrors.TableOutOfService, outOfService.Error);
        Assert.Single(_fixture.Sessions.Items);
    }

    [Fact]
    public async Task A_session_closes_only_once_its_orders_are_done_and_frees_the_table()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        var id = await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2));
        await _fixture.ChangeGuestCount.HandleAsync(new ChangeGuestCountCommand(id, 4));
        _fixture.SessionsWithOpenOrders.Add(TableSessionId.From(id));

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.CloseSession.HandleAsync(new CloseTableSessionCommand(id)));
        _fixture.SessionsWithOpenOrders.Clear();
        await _fixture.CloseSession.HandleAsync(new CloseTableSessionCommand(id));
        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.CloseSession.HandleAsync(new CloseTableSessionCommand(id)));

        Assert.Equal(TableSessionErrors.HasOpenOrders, refused.Error);
        Assert.Equal(TableSessionErrors.AlreadyClosed, again.Error);
        var read = await _fixture.GetSession.HandleAsync(new GetTableSessionByIdQuery(id));
        Assert.False(read.IsOpen);
        Assert.Equal(4, read.GuestCount);
        Assert.Null(Assert.Single((await _fixture.GetArea.HandleAsync(new GetDiningAreaByIdQuery(area))).Tables).OpenSessionId);
        await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2));
    }

    [Fact]
    public async Task An_occupied_table_cannot_be_removed()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        var session = await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, 2));

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.RemoveTable.HandleAsync(new RemoveTableCommand(area, table)));
        await _fixture.CloseSession.HandleAsync(new CloseTableSessionCommand(session));
        await _fixture.RemoveTable.HandleAsync(new RemoveTableCommand(area, table));

        Assert.Equal(DiningAreaErrors.TableHasOpenSession, refused.Error);
        Assert.Empty(_fixture.Areas.Stored(area).Tables);
    }

    [Fact]
    public async Task The_floor_plan_lists_the_areas_of_a_location_by_name()
    {
        var location = _fixture.GivenLocation();
        await _fixture.CreateArea.HandleAsync(new CreateDiningAreaCommand(location, "Terrasse"));
        await _fixture.CreateArea.HandleAsync(new CreateDiningAreaCommand(location, "Bar"));
        await _fixture.GivenTableAsync();
        await _fixture.GivenTableAsync(_fixture.TenantB);

        var atLocation = await _fixture.ListAreas.HandleAsync(new ListDiningAreasQuery(location));
        var all = await _fixture.ListAreas.HandleAsync(new ListDiningAreasQuery());
        var none = await _fixture.ListAreas.HandleAsync(new ListDiningAreasQuery(Guid.Empty));

        Assert.Equal(new[] { "Bar", "Terrasse" }, atLocation.Select(area => area.Name).ToArray());
        Assert.Equal(3, all.Count);
        Assert.Empty(none);
    }

    [Fact]
    public async Task A_table_is_renamed_placed_and_its_area_deleted_once_free()
    {
        var (areaId, tableId) = await _fixture.GivenTableAsync();

        await _fixture.RenameTable.HandleAsync(new RenameTableCommand(areaId, tableId, "Fenêtre"));
        await _fixture.PlaceTable.HandleAsync(new PlaceTableCommand(areaId, tableId, 5, 5, 10, 10, TableShape.Round));
        var area = await _fixture.GetArea.HandleAsync(new GetDiningAreaByIdQuery(areaId));
        var sessionId = await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(tableId, 2));
        var occupied = await Assert.ThrowsAsync<DomainException>(() => _fixture.DeleteArea.HandleAsync(new DeleteDiningAreaCommand(areaId)));
        await _fixture.CloseSession.HandleAsync(new CloseTableSessionCommand(sessionId));
        await _fixture.DeleteArea.HandleAsync(new DeleteDiningAreaCommand(areaId));

        var table = Assert.Single(area.Tables);
        Assert.Equal(("Fenêtre", new TableLayoutDto(5, 5, 10, 10, TableShape.Round)), (table.Label, table.Layout));
        Assert.Equal(DiningAreaErrors.AreaHasOpenSession, occupied.Error);
        Assert.Empty(_fixture.Areas.Items);
    }
}

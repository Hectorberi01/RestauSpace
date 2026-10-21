using Kitchen.Application.Commands;
using Kitchen.Application.Queries;
using Kitchen.Domain;

namespace Kitchen.Application;

public sealed class KitchenUseCaseTests
{
    private readonly KitchenFixture _fixture = new();

    [Fact]
    public async Task A_station_name_is_unique_in_its_location_whatever_the_case()
    {
        await _fixture.CreateStation.HandleAsync(new CreateStationCommand(_fixture.Location, "Grill"));

        var duplicate = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateStation.HandleAsync(new CreateStationCommand(_fixture.Location, "  GRILL ")));
        var elsewhere = await _fixture.CreateStation.HandleAsync(new CreateStationCommand(_fixture.GivenLocation(), "Grill"));
        var unknownLocation = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateStation.HandleAsync(new CreateStationCommand(Guid.NewGuid(), "Bar")));

        Assert.Equal(KitchenStationErrors.NameNotUnique, duplicate.Error);
        Assert.NotEqual(Guid.Empty, elsewhere);
        Assert.Equal(KitchenApplicationErrors.LocationNotFound, unknownLocation.Error);
        Assert.Equal(2, _fixture.Stations.Count);
    }

    [Fact]
    public async Task Stations_are_listed_by_name_and_renamed()
    {
        var (grill, _) = await _fixture.GivenKitchenAsync();

        await _fixture.RenameStation.HandleAsync(new RenameStationCommand(grill, "Plancha"));
        var stations = await _fixture.ListStations.HandleAsync(new ListStationsQuery(_fixture.Location));

        Assert.Equal(new[] { "Bar", "Plancha" }, stations.Select(station => station.Name).ToArray());
        Assert.All(stations, station => Assert.True(station.IsActive));
    }

    [Fact]
    public async Task A_location_has_one_routing_and_a_route_needs_a_product_of_the_tenant()
    {
        var (grill, bar) = await _fixture.GivenKitchenAsync();

        var second = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateRouting.HandleAsync(new CreateStationRoutingCommand(_fixture.Location, bar)));
        var unknownProduct = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.SetRoute.HandleAsync(new SetStationRouteCommand(_fixture.Location, Guid.NewGuid(), bar)));
        var foreignProduct = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.SetRoute.HandleAsync(
            new SetStationRouteCommand(_fixture.Location, _fixture.GivenProduct(_fixture.TenantB), bar)));
        var juice = _fixture.GivenProduct();
        await _fixture.SetRoute.HandleAsync(new SetStationRouteCommand(_fixture.Location, juice, bar));

        var routing = await _fixture.GetRouting.HandleAsync(new GetStationRoutingQuery(_fixture.Location));
        Assert.Equal(StationRoutingErrors.LocationAlreadyHasRouting, second.Error);
        Assert.Equal(KitchenApplicationErrors.ProductNotFound, unknownProduct.Error);
        Assert.Equal(KitchenApplicationErrors.ProductNotFound, foreignProduct.Error);
        Assert.Equal(grill, routing.DefaultStationId);
        Assert.Equal(new StationRouteDto(juice, bar), Assert.Single(routing.Routes));
    }

    [Fact]
    public async Task A_station_used_by_the_routing_cannot_be_deactivated()
    {
        var (grill, bar) = await _fixture.GivenKitchenAsync();
        var juice = _fixture.GivenProduct();
        await _fixture.SetRoute.HandleAsync(new SetStationRouteCommand(_fixture.Location, juice, bar));

        var byDefault = await Assert.ThrowsAsync<DomainException>(() => _fixture.DeactivateStation.HandleAsync(new DeactivateStationCommand(grill)));
        var byRoute = await Assert.ThrowsAsync<DomainException>(() => _fixture.DeactivateStation.HandleAsync(new DeactivateStationCommand(bar)));
        await _fixture.RemoveRoute.HandleAsync(new RemoveStationRouteCommand(_fixture.Location, juice));
        await _fixture.DeactivateStation.HandleAsync(new DeactivateStationCommand(bar));

        Assert.Equal(KitchenApplicationErrors.StationInUse, byDefault.Error);
        Assert.Equal(KitchenApplicationErrors.StationInUse, byRoute.Error);
        Assert.False(_fixture.Stations.Single(station => station.Id.Value == bar).IsActive);
        var inactive = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangeDefault.HandleAsync(new ChangeDefaultStationCommand(_fixture.Location, bar)));
        Assert.Equal(StationRoutingErrors.StationInactive, inactive.Error);
    }

    [Fact]
    public async Task An_accepted_order_becomes_one_ticket_whose_lines_go_to_their_station()
    {
        var (grill, bar) = await _fixture.GivenKitchenAsync();
        var juice = _fixture.GivenProduct();
        await _fixture.SetRoute.HandleAsync(new SetStationRouteCommand(_fixture.Location, juice, bar));
        var order = _fixture.GivenOrder(new[] { _fixture.GivenProduct(), juice });

        var id = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(order));
        var again = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(order));

        var ticket = await _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(id));
        Assert.Equal(id, again);
        Assert.Single(_fixture.Tickets);
        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        Assert.Equal(KitchenPriority.Normal, ticket.Priority);
        Assert.Equal(new[] { grill, bar }, ticket.Items.Select(item => item.StationId).ToArray());
        Assert.Equal(new[] { "Plat 1", "Plat 2" }, ticket.Items.Select(item => item.Name).ToArray());
        Assert.Equal("sans piment", ticket.Items[0].Modifiers.Single());
        DomainAssert.Raised<KitchenTicketCreated>(_fixture.StoredTicket(id));
    }

    [Fact]
    public async Task Only_an_accepted_order_of_the_tenant_with_a_configured_kitchen_gets_a_ticket()
    {
        var notAccepted = _fixture.GivenOrder(new[] { _fixture.GivenProduct() }, isAccepted: false);
        var ofTenantA = _fixture.GivenOrder(new[] { _fixture.GivenProduct() });
        await _fixture.GivenKitchenAsync();

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(notAccepted)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(Guid.NewGuid())));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(ofTenantA)));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var otherLocation = _fixture.GivenOrder(new[] { _fixture.GivenProduct() }, locationId: _fixture.GivenLocation());
        var unconfigured = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(otherLocation)));

        Assert.Equal(KitchenApplicationErrors.OrderNotAccepted, refused.Error);
        Assert.Equal(KitchenApplicationErrors.OrderNotFound, unknown.Error);
        Assert.Equal(KitchenApplicationErrors.OrderNotFound, foreign.Error);
        Assert.Equal(KitchenApplicationErrors.RoutingNotFound, unconfigured.Error);
        Assert.Empty(_fixture.Tickets);
    }

    [Fact]
    public async Task A_ticket_goes_from_the_queue_to_the_pass_line_by_line()
    {
        await _fixture.GivenKitchenAsync();
        var id = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(
            _fixture.GivenOrder(new[] { _fixture.GivenProduct(), _fixture.GivenProduct() })));
        var items = _fixture.StoredTicket(id).Items.Select(item => item.Id.Value).ToList();

        await _fixture.StartItem.HandleAsync(new StartTicketItemCommand(id, items[0]));
        await _fixture.ItemReady.HandleAsync(new MarkTicketItemReadyCommand(id, items[0]));
        var tooEarly = await Assert.ThrowsAsync<DomainException>(() => _fixture.TicketReady.HandleAsync(new MarkTicketReadyCommand(id)));
        await _fixture.StartItem.HandleAsync(new StartTicketItemCommand(id, items[1]));
        await _fixture.ItemReady.HandleAsync(new MarkTicketItemReadyCommand(id, items[1]));
        var ready = await _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(id));
        await _fixture.TicketReady.HandleAsync(new MarkTicketReadyCommand(id));
        await _fixture.Complete.HandleAsync(new CompleteTicketCommand(id));

        var done = await _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(id));
        Assert.Equal(KitchenTicketErrors.ItemsNotReady, tooEarly.Error);
        Assert.True(ready.CanBeMarkedReady);
        Assert.Equal(KitchenTicketStatus.Completed, done.Status);
        Assert.All(done.Items, item => Assert.Equal(KitchenTicketItemStatus.Ready, item.Status));
        Assert.NotNull(done.StartedAtUtc);
        Assert.NotNull(done.CompletedAtUtc);
        DomainAssert.Raised<KitchenPreparationStarted>(_fixture.StoredTicket(id));
        DomainAssert.Raised<KitchenTicketReady>(_fixture.StoredTicket(id));
        Assert.Empty(await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery(_fixture.Location)));
    }

    [Fact]
    public async Task The_kitchen_screen_shows_open_tickets_by_priority_then_age_and_can_filter_by_station()
    {
        var (_, bar) = await _fixture.GivenKitchenAsync();
        var juice = _fixture.GivenProduct();
        await _fixture.SetRoute.HandleAsync(new SetStationRouteCommand(_fixture.Location, juice, bar));
        var first = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { _fixture.GivenProduct() })));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { juice })));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var rush = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { _fixture.GivenProduct() })));
        await _fixture.ChangePriority.HandleAsync(new ChangeTicketPriorityCommand(rush, KitchenPriority.Rush));
        var cancelled = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { juice })));
        await _fixture.CancelTicket.HandleAsync(new CancelTicketCommand(cancelled, "Rupture"));

        var all = await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery(_fixture.Location));
        var atBar = await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery(_fixture.Location, bar));
        var empty = await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery(Guid.Empty));

        Assert.Equal(new[] { rush, first, second }, all.Select(ticket => ticket.Id).ToArray());
        Assert.Equal(second, Assert.Single(atBar).Id);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task Cancelling_an_order_cancels_its_open_ticket_even_for_a_suspended_restaurant()
    {
        await _fixture.GivenKitchenAsync();
        var order = _fixture.GivenOrder(new[] { _fixture.GivenProduct() });
        var id = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(order));
        _fixture.Writable[_fixture.TenantA] = false;

        await _fixture.CancelTicketForOrder.HandleAsync(new CancelTicketForOrderCommand(order, new string('x', 600)));
        await _fixture.CancelTicketForOrder.HandleAsync(new CancelTicketForOrderCommand(order, "deux fois"));
        await _fixture.CancelTicketForOrder.HandleAsync(new CancelTicketForOrderCommand(Guid.NewGuid(), "sans ticket"));

        var ticket = _fixture.StoredTicket(id);
        Assert.Equal(KitchenTicketStatus.Cancelled, ticket.Status);
        Assert.Equal(KitchenTicket.MaxCancellationReasonLength, ticket.CancellationReason!.Length);
        Assert.Single(ticket.DomainEvents.OfType<KitchenTicketCancelled>());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_kitchen_but_cannot_change_it()
    {
        await _fixture.GivenKitchenAsync();
        var id = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { _fixture.GivenProduct() })));
        _fixture.Writable[_fixture.TenantA] = false;

        var station = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateStation.HandleAsync(new CreateStationCommand(_fixture.Location, "Pâtisserie")));
        var start = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartTicket.HandleAsync(new StartTicketCommand(id)));

        Assert.Equal(KitchenApplicationErrors.TenantNotWritable, station.Error);
        Assert.Equal(KitchenApplicationErrors.TenantNotWritable, start.Error);
        Assert.Single(await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery(_fixture.Location)));
        Assert.Equal(2, (await _fixture.ListStations.HandleAsync(new ListStationsQuery(_fixture.Location))).Count);
    }

    [Fact]
    public async Task The_kitchen_of_another_restaurant_is_invisible()
    {
        var (grill, _) = await _fixture.GivenKitchenAsync();
        var id = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder(new[] { _fixture.GivenProduct() })));
        _fixture.CurrentTenantId = _fixture.TenantB;

        var ticket = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(id)));
        var station = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RenameStation.HandleAsync(new RenameStationCommand(grill, "Volé")));
        var location = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ListStations.HandleAsync(new ListStationsQuery(_fixture.Location)));

        Assert.Equal(KitchenApplicationErrors.TicketNotFound, ticket.Error);
        Assert.Equal(KitchenApplicationErrors.StationNotFound, station.Error);
        Assert.Equal(KitchenApplicationErrors.LocationNotFound, location.Error);
        Assert.Empty(await _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery()));
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_is_done()
    {
        _fixture.CurrentTenantId = null;

        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.ListOpen.HandleAsync(new ListOpenTicketsQuery()));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.CancelTicketForOrder.HandleAsync(new CancelTicketForOrderCommand(Guid.NewGuid(), null)));
    }

    [Fact]
    public async Task At_table_the_mains_wait_until_the_dining_room_sends_them()
    {
        await _fixture.GivenKitchenAsync();
        var (drink, starter, main) = (_fixture.GivenProduct(), _fixture.GivenProduct(), _fixture.GivenProduct());
        await _fixture.SetCourse.HandleAsync(new SetProductCourseCommand(_fixture.Location, starter, KitchenCourse.Starter));
        await _fixture.SetCourse.HandleAsync(new SetProductCourseCommand(_fixture.Location, main, KitchenCourse.Main));

        var atTable = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder([drink, starter, main], isDineIn: true)));
        var takeaway = await _fixture.CreateTicket.HandleAsync(new CreateTicketForOrderCommand(_fixture.GivenOrder([starter, main])));
        var waiting = await _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(atTable));
        var held = waiting.Items.Single(item => item.IsHeld);
        var early = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartItem.HandleAsync(new StartTicketItemCommand(atTable, held.Id)));
        await _fixture.FireCourse.HandleAsync(new FireCourseCommand(atTable, KitchenCourse.Main));
        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.FireCourse.HandleAsync(new FireCourseCommand(atTable, KitchenCourse.Main)));
        await _fixture.StartItem.HandleAsync(new StartTicketItemCommand(atTable, held.Id));

        var routing = await _fixture.GetRouting.HandleAsync(new GetStationRoutingQuery(_fixture.Location));
        Assert.Equal(KitchenCourse.Main, held.Course);
        Assert.Equal([KitchenCourse.Main], waiting.HeldCourses);
        Assert.DoesNotContain((await _fixture.GetTicket.HandleAsync(new GetTicketByIdQuery(takeaway))).Items, item => item.IsHeld);
        Assert.Equal(KitchenTicketErrors.ItemHeld, early.Error);
        Assert.Equal(KitchenTicketErrors.NothingToFire, again.Error);
        Assert.Equal(2, routing.Courses!.Count);
    }
}

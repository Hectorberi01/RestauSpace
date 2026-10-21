namespace Kitchen.Domain;

public class StationRoutingTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly LocationReference LocationRef = KitchenRefs.Location(Tenant, Location);
    private readonly KitchenStation _kitchen = KitchenRefs.NewStation(Tenant, Location, "Kitchen");
    private readonly KitchenStation _grill = KitchenRefs.NewStation(Tenant, Location, "Grill");
    private readonly KitchenStation _bar = KitchenRefs.NewStation(Tenant, Location, "Bar");
    private readonly FakeClock _clock = new();

    private KitchenStationReference Kitchen => _kitchen.ToReference();

    private KitchenStationReference Grill => _grill.ToReference();

    private KitchenStationReference Bar => _bar.ToReference();

    private static KitchenStationReference InactiveStation() => KitchenRefs.Station(Tenant, Location, isActive: false);

    /// <summary>Routage dont la station par défaut est <see cref="Kitchen"/>, événements effacés.</summary>
    private StationRouting NewRouting()
    {
        var routing = StationRouting.Create(Tenant, LocationRef, Kitchen, isOnlyRoutingOfLocation: true, _clock);
        routing.ClearDomainEvents();
        return routing;
    }

    // ---- Create ----

    [Fact]
    public void Create_builds_a_routing_without_route_and_raises_StationRoutingCreated()
    {
        var routing = StationRouting.Create(Tenant, LocationRef, Kitchen, isOnlyRoutingOfLocation: true, _clock);

        Assert.Equal(Tenant, routing.TenantId);
        Assert.Equal(Location, routing.LocationId);
        Assert.Equal(Kitchen.Id, routing.DefaultStationId);
        Assert.Empty(routing.Routes);

        var created = DomainAssert.Raised<StationRoutingCreated>(routing);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(routing.Id, created.RoutingId);
        Assert.Equal(Location, created.LocationId);
        Assert.Equal(Kitchen.Id, created.DefaultStationId);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_accepts_the_reference_of_a_real_station()
    {
        var station = KitchenStation.Create(Tenant, LocationRef, StationName.Create("Kitchen"), true, _clock);

        var routing = StationRouting.Create(Tenant, LocationRef, station.ToReference(), true, _clock);

        Assert.Equal(station.Id, routing.DefaultStationId);
    }

    [Fact]
    public void Create_rejects_a_second_routing_for_the_location() =>
        DomainAssert.Throws(
            StationRoutingErrors.LocationAlreadyHasRouting,
            () => StationRouting.Create(Tenant, LocationRef, Kitchen, isOnlyRoutingOfLocation: false, _clock));

    [Fact]
    public void Create_rejects_empty_tenant_or_location()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => StationRouting.Create(default, LocationRef, Kitchen, true, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Location, true));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant_and_accepts_one_that_is_not_operational()
    {
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => StationRouting.Create(Tenant, KitchenRefs.Location(TenantId.New(), Location), Kitchen, true, _clock));

        var routing = StationRouting.Create(Tenant, new LocationReference(Tenant, Location, IsOperational: false), Kitchen, true, _clock);

        Assert.Equal(Location, routing.LocationId);
    }

    [Fact]
    public void Create_rejects_a_default_station_of_another_tenant_or_location_or_inactive()
    {
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => StationRouting.Create(Tenant, LocationRef, KitchenRefs.Station(TenantId.New(), Location), true, _clock));
        DomainAssert.Throws(
            StationRoutingErrors.StationOtherLocation,
            () => StationRouting.Create(Tenant, LocationRef, KitchenRefs.Station(Tenant, LocationId.New()), true, _clock));
        DomainAssert.Throws(
            StationRoutingErrors.StationInactive,
            () => StationRouting.Create(Tenant, LocationRef, InactiveStation(), true, _clock));
    }

    // ---- Station par défaut ----

    [Fact]
    public void ChangeDefaultStation_replaces_it_and_raises_StationRoutingDefaultStationChanged()
    {
        var routing = NewRouting();
        _clock.Advance(TimeSpan.FromMinutes(5));

        routing.ChangeDefaultStation(Grill, _clock);

        Assert.Equal(Grill.Id, routing.DefaultStationId);
        Assert.Equal(Grill.Id, routing.StationFor(ProductId.New()));
        var changed = DomainAssert.Raised<StationRoutingDefaultStationChanged>(routing);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(routing.Id, changed.RoutingId);
        Assert.Equal(Location, changed.LocationId);
        Assert.Equal(Kitchen.Id, changed.PreviousStationId);
        Assert.Equal(Grill.Id, changed.NewStationId);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void ChangeDefaultStation_to_the_same_station_is_a_no_op()
    {
        var routing = NewRouting();

        routing.ChangeDefaultStation(Kitchen, _clock);
        _kitchen.Deactivate(_clock);
        routing.ChangeDefaultStation(Kitchen, _clock);

        Assert.Equal(Kitchen.Id, routing.DefaultStationId);
        DomainAssert.NoEvents(routing);
    }

    [Fact]
    public void ChangeDefaultStation_rejects_a_station_of_another_tenant_or_location_or_inactive()
    {
        var routing = NewRouting();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => routing.ChangeDefaultStation(KitchenRefs.Station(TenantId.New(), Location), _clock));
        DomainAssert.Throws(StationRoutingErrors.StationOtherLocation, () => routing.ChangeDefaultStation(KitchenRefs.Station(Tenant, LocationId.New()), _clock));
        DomainAssert.Throws(StationRoutingErrors.StationInactive, () => routing.ChangeDefaultStation(InactiveStation(), _clock));

        Assert.Equal(Kitchen.Id, routing.DefaultStationId);
        DomainAssert.NoEvents(routing);
    }

    // ---- Routes ----

    [Fact]
    public void SetRoute_adds_a_route_and_raises_StationRouteSet()
    {
        var routing = NewRouting();
        var burger = ProductId.New();

        routing.SetRoute(burger, Grill, _clock);

        Assert.Equal([new StationRoute(burger, Grill.Id)], routing.Routes);
        var set = DomainAssert.Raised<StationRouteSet>(routing);
        Assert.Equal(Tenant, set.TenantId);
        Assert.Equal(routing.Id, set.RoutingId);
        Assert.Equal(Location, set.LocationId);
        Assert.Equal(burger, set.ProductId);
        Assert.Equal(Grill.Id, set.StationId);
        Assert.Null(set.PreviousStationId);
        Assert.Equal(_clock.UtcNow, set.OccurredAtUtc);
    }

    [Fact]
    public void SetRoute_replaces_the_station_of_an_already_routed_product()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);
        routing.ClearDomainEvents();

        routing.SetRoute(burger, Bar, _clock);

        Assert.Equal([new StationRoute(burger, Bar.Id)], routing.Routes);
        var set = DomainAssert.Raised<StationRouteSet>(routing);
        Assert.Equal(Bar.Id, set.StationId);
        Assert.Equal(Grill.Id, set.PreviousStationId);
    }

    [Fact]
    public void SetRoute_to_the_station_already_routed_is_a_no_op()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);
        routing.ClearDomainEvents();

        routing.SetRoute(burger, Grill, _clock);
        _grill.Deactivate(_clock);
        routing.SetRoute(burger, Grill, _clock);

        Assert.Single(routing.Routes);
        DomainAssert.NoEvents(routing);
    }

    [Fact]
    public void SetRoute_may_route_a_product_explicitly_to_the_default_station()
    {
        var routing = NewRouting();
        var burger = ProductId.New();

        routing.SetRoute(burger, Kitchen, _clock);
        routing.ChangeDefaultStation(Bar, _clock);

        Assert.Equal(Kitchen.Id, routing.StationFor(burger));
    }

    [Fact]
    public void SetRoute_rejects_a_station_of_another_tenant_or_location_or_inactive_and_an_empty_product()
    {
        var routing = NewRouting();
        var burger = ProductId.New();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => routing.SetRoute(burger, KitchenRefs.Station(TenantId.New(), Location), _clock));
        DomainAssert.Throws(StationRoutingErrors.StationOtherLocation, () => routing.SetRoute(burger, KitchenRefs.Station(Tenant, LocationId.New()), _clock));
        DomainAssert.Throws(StationRoutingErrors.StationInactive, () => routing.SetRoute(burger, InactiveStation(), _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => routing.SetRoute(default, Grill, _clock));

        Assert.Empty(routing.Routes);
        DomainAssert.NoEvents(routing);
    }

    [Fact]
    public void SetRoute_rejects_a_new_route_beyond_the_limit_but_still_replaces_existing_ones()
    {
        var routing = NewRouting();
        var products = Enumerable.Range(0, StationRouting.MaxRoutes).Select(_ => ProductId.New()).ToList();
        products.ForEach(p => routing.SetRoute(p, Grill, _clock));
        routing.ClearDomainEvents();

        DomainAssert.Throws(StationRoutingErrors.RouteLimitReached, () => routing.SetRoute(ProductId.New(), Grill, _clock));
        Assert.Equal(StationRouting.MaxRoutes, routing.Routes.Count);
        DomainAssert.NoEvents(routing);

        routing.SetRoute(products[0], Bar, _clock);
        Assert.Equal(Bar.Id, routing.StationFor(products[0]));
    }

    [Fact]
    public void RemoveRoute_sends_the_product_back_to_the_default_station_and_raises_StationRouteRemoved()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);
        routing.ClearDomainEvents();

        routing.RemoveRoute(burger, _clock);

        Assert.Empty(routing.Routes);
        Assert.Equal(Kitchen.Id, routing.StationFor(burger));
        var removed = DomainAssert.Raised<StationRouteRemoved>(routing);
        Assert.Equal(Tenant, removed.TenantId);
        Assert.Equal(routing.Id, removed.RoutingId);
        Assert.Equal(Location, removed.LocationId);
        Assert.Equal(burger, removed.ProductId);
        Assert.Equal(Grill.Id, removed.StationId);
    }

    [Fact]
    public void RemoveRoute_rejects_a_product_without_route()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);
        routing.RemoveRoute(burger, _clock);
        routing.ClearDomainEvents();

        DomainAssert.Throws(StationRoutingErrors.RouteNotFound, () => routing.RemoveRoute(burger, _clock));

        DomainAssert.NoEvents(routing);
    }

    // ---- StationFor ----

    [Fact]
    public void StationFor_returns_the_route_or_falls_back_to_the_default_station()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        var beer = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);
        routing.SetRoute(beer, Bar, _clock);
        routing.ClearDomainEvents();

        Assert.Equal(Grill.Id, routing.StationFor(burger));
        Assert.Equal(Bar.Id, routing.StationFor(beer));
        Assert.Equal(Kitchen.Id, routing.StationFor(ProductId.New()));
        DomainAssert.NoEvents(routing);
    }

    [Fact]
    public void An_order_made_only_of_unrouted_products_is_fully_routable_to_the_default_station()
    {
        var routing = NewRouting();
        var drinks = new[] { ProductId.New(), ProductId.New() };

        var lines = drinks.Select(p =>
        {
            Assert.Equal(Kitchen.Id, routing.StationFor(p));
            return KitchenOrderLine.Create(OrderItemId.New(), "Drink", 1, Kitchen, null, [], null);
        });
        var ticket = KitchenTicket.CreateFromOrder(
            KitchenOrderSnapshot.Create(OrderId.New(), Tenant, Location, lines), KitchenPriority.Normal, _clock);

        Assert.All(ticket.Items, i => Assert.Equal(routing.DefaultStationId, i.StationId));
    }

    [Fact]
    public void Uses_tells_whether_a_station_is_the_default_one_or_a_route_target()
    {
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, Grill, _clock);

        Assert.True(routing.Uses(Kitchen.Id));
        Assert.True(routing.Uses(Grill.Id));
        Assert.False(routing.Uses(Bar.Id));

        routing.RemoveRoute(burger, _clock);
        Assert.False(routing.Uses(Grill.Id));
    }

    [Fact]
    public void A_station_deactivated_after_routing_stays_routed_and_is_refused_at_ticket_creation()
    {
        var station = KitchenStation.Create(Tenant, LocationRef, StationName.Create("Grill"), true, _clock);
        var routing = NewRouting();
        var burger = ProductId.New();
        routing.SetRoute(burger, station.ToReference(), _clock);

        station.Deactivate(_clock);

        Assert.Equal(station.Id, routing.StationFor(burger));
        Assert.True(routing.Uses(station.Id));
        var line = KitchenOrderLine.Create(OrderItemId.New(), "Burger", 1, station.ToReference(), null, [], null);
        DomainAssert.Throws(
            KitchenTicketErrors.StationInactive,
            () => KitchenTicket.CreateFromOrder(
                KitchenOrderSnapshot.Create(OrderId.New(), Tenant, Location, [line]), KitchenPriority.Normal, _clock));
    }

    [Fact]
    public void A_product_gets_a_course_and_back_to_direct()
    {
        var routing = NewRouting();
        var product = ProductId.New();

        routing.SetCourse(product, KitchenCourse.Dessert, _clock);
        routing.SetCourse(product, KitchenCourse.Dessert, _clock);
        var dessert = routing.CourseFor(product);
        routing.SetCourse(product, KitchenCourse.Direct, _clock);

        Assert.Equal(KitchenCourse.Dessert, dessert);
        Assert.Empty(routing.Courses);
        Assert.Equal(2, routing.DomainEvents.OfType<ProductCourseSet>().Count());
        DomainAssert.Throws(KitchenTicketErrors.CourseUnknown, () => routing.SetCourse(product, (KitchenCourse)7, _clock));
    }
}

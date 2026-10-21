using Kitchen.Application;
using Kitchen.Application.Abstractions;
using Kitchen.Domain;
using Kitchen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kitchen.Infrastructure;

public sealed class KitchenPersistenceTests(KitchenDatabase database) : IClassFixture<KitchenDatabase>
{
    private IClock Clock => database.Clock;

    private static LocationReference Location(TenantId tenantId, LocationId locationId) => new(tenantId, locationId, IsOperational: true);

    private KitchenStation NewStation(TenantId tenantId, LocationId locationId, string name) =>
        KitchenStation.Create(tenantId, Location(tenantId, locationId), StationName.Create(name), isNameUniqueInLocation: true, Clock);

    private KitchenTicket NewTicket(TenantId tenantId, LocationId locationId, KitchenPriority priority, params KitchenStation[] stations) =>
        KitchenTicket.CreateFromOrder(
            KitchenOrderSnapshot.Create(
                OrderId.New(),
                tenantId,
                locationId,
                stations.Select((station, index) => KitchenOrderLine.Create(
                    OrderItemId.New(), $"Plat {index + 1}", index + 1, station.ToReference(), index == 0 ? "Grande" : null,
                    index == 0 ? new[] { "sans piment", "bien cuit" } : Array.Empty<string>(), index == 0 ? "allergie arachide" : null))),
            priority,
            Clock);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    private async Task<(KitchenStation Grill, KitchenStation Bar)> GivenStationsAsync(TenantId tenantId, LocationId locationId)
    {
        var grill = NewStation(tenantId, locationId, "Grill");
        var bar = NewStation(tenantId, locationId, "Bar");
        await SaveAsync(tenantId, services =>
        {
            services.GetRequiredService<IKitchenStationRepository>().Add(grill);
            services.GetRequiredService<IKitchenStationRepository>().Add(bar);
        });
        return (grill, bar);
    }

    [Fact]
    public async Task Stations_and_a_routing_with_its_routes_come_back_whole()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, bar) = await GivenStationsAsync(tenant, location);
        var routing = StationRouting.Create(tenant, Location(tenant, location), grill.ToReference(), isOnlyRoutingOfLocation: true, Clock);
        var products = new[] { ProductId.New(), ProductId.New() };
        foreach (var product in products)
        {
            routing.SetRoute(product, bar.ToReference(), Clock);
        }

        routing.SetCourse(products[0], KitchenCourse.Main, Clock);

        await SaveAsync(tenant, services => services.GetRequiredService<IStationRoutingRepository>().Add(routing));

        var stations = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenStationRepository>().ListAsync(tenant, location));
        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IStationRoutingRepository>().FindByLocationAsync(tenant, location));
        var many = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenStationRepository>().FindManyAsync(tenant, new[] { grill.Id, bar.Id, grill.Id }));

        Assert.Equal(new[] { "Bar", "Grill" }, stations.Select(station => station.Name.Value).ToArray());
        Assert.NotNull(loaded);
        Assert.Equal(grill.Id, loaded.DefaultStationId);
        Assert.Equal(products, loaded.Routes.Select(route => route.ProductId).ToArray());
        Assert.All(loaded.Routes, route => Assert.Equal(bar.Id, route.StationId));
        Assert.Equal(bar.Id, loaded.StationFor(products[1]));
        Assert.Equal(grill.Id, loaded.StationFor(ProductId.New()));
        Assert.Equal(2, many.Count);
        Assert.Equal(KitchenCourse.Main, loaded.CourseFor(products[0]));
        Assert.Equal(KitchenCourse.Direct, loaded.CourseFor(products[1]));
    }

    [Fact]
    public async Task A_held_course_comes_back_held_and_fired_once_sent()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, _) = await GivenStationsAsync(tenant, location);
        var ticket = KitchenTicket.CreateFromOrder(
            KitchenOrderSnapshot.Create(OrderId.New(), tenant, location, new[]
            {
                KitchenOrderLine.Create(OrderItemId.New(), "Salade", 1, grill.ToReference(), null, [], null, KitchenCourse.Starter),
                KitchenOrderLine.Create(OrderItemId.New(), "Poulet", 1, grill.ToReference(), null, [], null, KitchenCourse.Main),
            }),
            KitchenPriority.Normal,
            Clock,
            holdLaterCourses: true);
        await SaveAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().Add(ticket));

        var held = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id));
        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id);
            loaded!.FireCourse(KitchenCourse.Main, Clock);
            await scope.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync();
        }

        var fired = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id));
        Assert.Equal([KitchenCourse.Starter, KitchenCourse.Main], held!.Items.Select(item => item.Course).ToArray());
        Assert.Equal([KitchenCourse.Main], held.HeldCourses);
        Assert.Empty(fired!.HeldCourses);
    }

    [Fact]
    public async Task Station_names_and_routings_are_unique_per_location_even_under_a_race()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, _) = await GivenStationsAsync(tenant, location);

        var checker = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenStationNameUniquenessChecker>()
            .IsUniqueAsync(tenant, location, StationName.Create("GRILL"), null));
        var self = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenStationNameUniquenessChecker>()
            .IsUniqueAsync(tenant, location, StationName.Create("grill"), grill.Id));
        // Deux requêtes ont toutes deux vu le nom libre : l'index unique arrête la seconde.
        var name = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(
            tenant, services => services.GetRequiredService<IKitchenStationRepository>().Add(NewStation(tenant, location, "grill"))));

        await SaveAsync(tenant, services => services.GetRequiredService<IStationRoutingRepository>().Add(
            StationRouting.Create(tenant, Location(tenant, location), grill.ToReference(), isOnlyRoutingOfLocation: true, Clock)));
        var routingFree = await InScopeAsync(tenant, services => services.GetRequiredService<IStationRoutingUniquenessChecker>()
            .IsUniqueAsync(tenant, location));
        var routing = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, services =>
            services.GetRequiredService<IStationRoutingRepository>().Add(
                StationRouting.Create(tenant, Location(tenant, location), grill.ToReference(), isOnlyRoutingOfLocation: true, Clock))));

        Assert.False(checker);
        Assert.True(self);
        Assert.Equal(KitchenStationErrors.NameNotUnique, name.Error);
        Assert.False(routingFree);
        Assert.Equal(StationRoutingErrors.LocationAlreadyHasRouting, routing.Error);
    }

    [Fact]
    public async Task A_ticket_comes_back_with_its_lines_in_order_and_its_events_in_the_outbox()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, bar) = await GivenStationsAsync(tenant, location);
        // Plusieurs lignes dans la même milliseconde : seul l'ordre de la liste fait foi, pas celui des ids.
        var ticket = NewTicket(tenant, location, KitchenPriority.High, grill, bar, grill, bar);
        await SaveAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().Add(ticket));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id);
            loaded!.MarkItemStarted(loaded.Items.First().Id, Clock);
            await scope.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().FindByOrderAsync(tenant, ticket.OrderId));
        Assert.NotNull(reloaded);
        Assert.Equal(new[] { "Plat 1", "Plat 2", "Plat 3", "Plat 4" }, reloaded.Items.Select(item => item.Name).ToArray());
        Assert.Equal(new[] { grill.Id, bar.Id, grill.Id, bar.Id }, reloaded.Items.Select(item => item.StationId).ToArray());
        var first = reloaded.Items.First();
        Assert.Equal(new[] { "sans piment", "bien cuit" }, first.Modifiers.ToArray());
        Assert.Equal("Grande", first.VariantName);
        Assert.Equal("allergie arachide", first.Note);
        Assert.Equal(1, first.Quantity);
        Assert.Equal(KitchenTicketItemStatus.Preparing, first.Status);
        Assert.Equal(KitchenTicketStatus.InProgress, reloaded.Status);
        Assert.Equal(KitchenPriority.High, reloaded.Priority);
        Assert.Equal(100, reloaded.ReadinessPolicy.RequiredReadyPercentage);

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<KitchenDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(KitchenTicketCreated), types);
        Assert.Contains(nameof(KitchenPreparationStarted), types);
        Assert.Contains(nameof(KitchenTicketItemStarted), types);
    }

    [Fact]
    public async Task An_order_has_at_most_one_ticket()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, _) = await GivenStationsAsync(tenant, location);
        var ticket = NewTicket(tenant, location, KitchenPriority.Normal, grill);
        await SaveAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().Add(ticket));

        var twin = KitchenTicket.CreateFromOrder(
            KitchenOrderSnapshot.Create(ticket.OrderId, tenant, location, new[]
            {
                KitchenOrderLine.Create(OrderItemId.New(), "Plat", 1, grill.ToReference(), null, Array.Empty<string>(), null),
            }),
            KitchenPriority.Normal,
            Clock);
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => SaveAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().Add(twin)));

        Assert.Equal(KitchenApplicationErrors.TicketAlreadyExists, refused.Error);
    }

    [Fact]
    public async Task The_kitchen_screen_lists_open_tickets_by_priority_then_age_with_a_station_filter()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, bar) = await GivenStationsAsync(tenant, location);
        var old = NewTicket(tenant, location, KitchenPriority.Normal, grill);
        database.Clock.Advance(TimeSpan.FromMinutes(1));
        var recent = NewTicket(tenant, location, KitchenPriority.Normal, grill, bar);
        database.Clock.Advance(TimeSpan.FromMinutes(1));
        var rush = NewTicket(tenant, location, KitchenPriority.Rush, grill);
        var done = NewTicket(tenant, location, KitchenPriority.Rush, bar);
        done.Cancel(Clock, "Rupture");
        await SaveAsync(tenant, services =>
        {
            var tickets = services.GetRequiredService<IKitchenTicketRepository>();
            foreach (var ticket in new[] { old, recent, rush, done })
            {
                tickets.Add(ticket);
            }
        });

        var all = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().ListOpenAsync(tenant, location, null));
        var atBar = await InScopeAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().ListOpenAsync(tenant, location, bar.Id));

        Assert.Equal(new[] { rush.Id, old.Id, recent.Id }, all.Select(ticket => ticket.Id).ToArray());
        Assert.Equal(recent.Id, Assert.Single(atBar).Id);
        Assert.Equal(2, atBar.Single().Items.Count);
    }

    [Fact]
    public async Task Two_stations_acting_on_the_same_ticket_at_once_cannot_both_win()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var (grill, bar) = await GivenStationsAsync(tenant, location);
        var ticket = NewTicket(tenant, location, KitchenPriority.Normal, grill, bar);
        await SaveAsync(tenant, services => services.GetRequiredService<IKitchenTicketRepository>().Add(ticket));
        await using (var scope = database.NewScope(tenant))
        {
            var started = await scope.ServiceProvider.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id);
            started!.Start(Clock);
            await scope.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync();
        }

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenant, ticket.Id);
        mine!.MarkItemStarted(mine.Items.First().Id, Clock);
        theirs!.MarkItemStarted(theirs.Items.Last().Id, Clock);

        await first.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync();
        // Seule une ligne change de chaque côté : c'est la version du ticket, réécrite, qui arrête la seconde.
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IKitchenUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task The_kitchen_of_another_tenant_cannot_be_loaded_even_with_its_ids()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var location = LocationId.New();
        var (grill, _) = await GivenStationsAsync(tenantA, location);
        var ticket = NewTicket(tenantA, location, KitchenPriority.Normal, grill);
        await SaveAsync(tenantA, services => services.GetRequiredService<IKitchenTicketRepository>().Add(ticket));

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IKitchenTicketRepository>().FindAsync(tenantA, ticket.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IKitchenStationRepository>().FindAsync(tenantB, grill.Id)));
        Assert.Empty(await InScopeAsync(tenantB, services => services.GetRequiredService<IKitchenTicketRepository>().ListOpenAsync(tenantB, location, null)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<KitchenDbContext>().Tickets.ToListAsync()));
    }
}

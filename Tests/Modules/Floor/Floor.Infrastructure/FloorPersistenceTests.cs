using Floor.Application.Abstractions;
using Floor.Domain;
using Floor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Floor.Infrastructure;

public sealed class FloorPersistenceTests(FloorDatabase database) : IClassFixture<FloorDatabase>
{
    private IClock Clock => database.Clock;

    private DiningArea NewArea(TenantId tenantId, string name = "Terrasse", LocationId? locationId = null) =>
        DiningArea.Create(tenantId, new LocationReference(tenantId, locationId ?? LocationId.New(), IsOperational: true), name, Clock);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    private async Task<(DiningArea Area, TableId Table)> GivenTableAsync(TenantId tenantId)
    {
        var area = NewArea(tenantId);
        var table = area.AddTable("T1", 4, Clock);
        await SaveAsync(tenantId, services => services.GetRequiredService<IDiningAreaRepository>().Add(area));
        return (area, table);
    }

    private static TableSession Open(DiningArea area, TableId table) =>
        TableSession.Open(area.TenantId, area.GetTableReference(table), 2, tableHasOpenSession: false, new FakeClock());

    [Fact]
    public async Task A_dining_area_comes_back_with_its_tables_in_order()
    {
        var tenant = TenantId.New();
        var area = NewArea(tenant);
        // Plusieurs tables dans la même milliseconde : seul l'ordre de la liste fait foi, pas celui des ids.
        var labels = new[] { "T3", "T1", "Bar 2", "T10" };
        foreach (var label in labels)
        {
            area.AddTable(label, 4, Clock);
        }

        area.SetTableOutOfService(area.Tables.Last().Id, Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().Add(area));

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id));
        var byTable = await InScopeAsync(
            tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindByTableAsync(tenant, area.Tables.First().Id));

        Assert.NotNull(loaded);
        Assert.Equal("Terrasse", loaded.Name);
        Assert.Equal(area.LocationId, loaded.LocationId);
        Assert.Equal(labels, loaded.Tables.Select(table => table.Label.Value).ToArray());
        Assert.Equal(TableStatus.OutOfService, loaded.Tables.Last().Status);
        Assert.Equal(12, loaded.SeatsInService);
        Assert.Equal(area.Id, byTable!.Id);
    }

    [Fact]
    public async Task Floor_rows_of_another_tenant_cannot_be_loaded_even_with_their_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var (area, table) = await GivenTableAsync(tenantA);
        var session = Open(area, table);
        await SaveAsync(tenantA, services => services.GetRequiredService<ITableSessionRepository>().Add(session));

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenantA, area.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IDiningAreaRepository>().FindByTableAsync(tenantB, table)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<ITableSessionRepository>().FindAsync(tenantA, session.Id)));
        Assert.False(await InScopeAsync(tenantB, services => services.GetRequiredService<ITableOccupancyChecker>().HasOpenSessionAsync(tenantB, table)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<FloorDbContext>().TableSessions.ToListAsync()));

        Assert.True(await InScopeAsync(tenantA, services => services.GetRequiredService<ITableOccupancyChecker>().HasOpenSessionAsync(tenantA, table)));
    }

    [Fact]
    public async Task A_session_comes_back_whole_and_closing_frees_the_table()
    {
        var tenant = TenantId.New();
        var area = NewArea(tenant);
        var table = area.AddTable("T1", 4, Clock);
        var reservation = ReservationReference.From(Guid.NewGuid());
        var session = TableSession.Open(tenant, area.GetTableReference(table), 3, false, Clock, reservation);
        var walkIn = Open(area, area.AddTable("T2", 2, Clock));
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IDiningAreaRepository>().Add(area);
            services.GetRequiredService<ITableSessionRepository>().Add(session);
            services.GetRequiredService<ITableSessionRepository>().Add(walkIn);
        });

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<ITableSessionRepository>().FindAsync(tenant, session.Id));
        Assert.Equal(reservation, loaded!.Reservation);
        Assert.Null((await InScopeAsync(
            tenant, services => services.GetRequiredService<ITableSessionRepository>().FindAsync(tenant, walkIn.Id)))!.Reservation);
        Assert.Equal(3, loaded.GuestCount);
        Assert.Equal(area.LocationId, loaded.LocationId);

        await using (var scope = database.NewScope(tenant))
        {
            var toClose = await scope.ServiceProvider.GetRequiredService<ITableSessionRepository>().FindAsync(tenant, session.Id);
            toClose!.Close(hasOpenOrders: false, Clock);
            await scope.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
        }

        var open = await InScopeAsync(tenant, services => services.GetRequiredService<ITableSessionRepository>().ListOpenAsync(tenant, area.LocationId));
        Assert.Equal(walkIn.Id, Assert.Single(open).Id);
        Assert.False(await InScopeAsync(tenant, services => services.GetRequiredService<ITableOccupancyChecker>().HasOpenSessionAsync(tenant, table)));
        // La table libérée accepte une nouvelle session : l'index unique ne porte que sur les sessions ouvertes.
        await SaveAsync(tenant, services => services.GetRequiredService<ITableSessionRepository>().Add(Open(area, table)));
    }

    [Fact]
    public async Task Two_requests_seating_the_same_table_cannot_both_win()
    {
        var tenant = TenantId.New();
        var (area, table) = await GivenTableAsync(tenant);
        await SaveAsync(tenant, services => services.GetRequiredService<ITableSessionRepository>().Add(Open(area, table)));

        // Le contrôle préalable a répondu « libre » aux deux requêtes : l'index unique tranche.
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => SaveAsync(tenant, services => services.GetRequiredService<ITableSessionRepository>().Add(Open(area, table))));

        Assert.Equal(TableSessionErrors.TableAlreadyOccupied, refused.Error);
    }

    [Fact]
    public async Task Two_requests_adding_a_table_with_the_same_label_cannot_both_win()
    {
        var tenant = TenantId.New();
        var (area, _) = await GivenTableAsync(tenant);

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id);
        mine!.AddTable("T2", 4, Clock);
        theirs!.AddTable("t2", 6, Clock);

        await first.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
        // La version de la zone, réécrite, ou l'index unique arrête la seconde.
        var lost = await Record.ExceptionAsync(() => second.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync());

        Assert.True(lost is ConcurrencyException or DomainException, lost?.ToString());
        var stored = await InScopeAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id));
        Assert.Equal(4, stored!.Tables.Single(table => table.Label.Value == "T2").Capacity);
    }

    [Fact]
    public async Task A_renamed_and_placed_table_comes_back_and_a_deleted_area_takes_its_tables()
    {
        var tenant = TenantId.New();
        var area = NewArea(tenant);
        var table = area.AddTable("T1", 4, Clock);
        area.AddTable("T2", 2, Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().Add(area));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id);
            loaded!.RenameTable(table, "Fenêtre", Clock);
            loaded.PlaceTable(table, TableLayout.Create(10, 12, 16, 8, TableShape.Rectangle, 90), Clock);
            await scope.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
        }

        var placed = await InScopeAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id));
        var first = placed!.Tables.First();
        Assert.Equal("Fenêtre", first.Label.Value);
        Assert.Equal(TableLayout.Create(10, 12, 16, 8, TableShape.Rectangle, 90), first.Layout);
        Assert.Null(placed.Tables.Last().Layout);

        await using (var scope = database.NewScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDiningAreaRepository>();
            var loaded = await repository.FindAsync(tenant, area.Id);
            loaded!.Delete([], Clock);
            repository.Remove(loaded);
            await scope.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
        }

        var gone = await InScopeAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id));
        var orphanTables = await InScopeAsync(tenant, services => services.GetRequiredService<FloorDbContext>().Set<RestaurantTable>()
            .CountAsync(row => EF.Property<DiningAreaId>(row, "DiningAreaId") == area.Id));
        var types = await InScopeAsync(tenant, services => services.GetRequiredService<FloorDbContext>().OutboxMessages.Select(message => message.Type).ToListAsync());
        Assert.Null(gone);
        Assert.Equal(0, orphanTables);
        Assert.Contains(nameof(TableRenamed), types);
        Assert.Contains(nameof(TablePlaced), types);
        Assert.Contains(nameof(DiningAreaDeleted), types);
    }

    [Fact]
    public async Task Removed_tables_are_deleted_and_events_reach_the_outbox()
    {
        var tenant = TenantId.New();
        var (area, table) = await GivenTableAsync(tenant);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id);
            loaded!.RemoveTable(table, hasOpenSession: false, Clock);
            loaded.Rename("Terrasse couverte", Clock);
            await scope.ServiceProvider.GetRequiredService<IFloorUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IDiningAreaRepository>().FindAsync(tenant, area.Id));
        Assert.Empty(reloaded!.Tables);
        Assert.Equal("Terrasse couverte", reloaded.Name);

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<FloorDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(TableAdded), types);
        Assert.Contains(nameof(TableRemoved), types);
        Assert.Contains(nameof(DiningAreaRenamed), types);
    }

    [Fact]
    public async Task A_session_stays_occupied_until_each_of_its_orders_is_closed_even_when_the_closure_arrives_first()
    {
        var tenant = TenantId.New();
        var session = TableSessionId.New();
        var first = Guid.NewGuid();
        var late = Guid.NewGuid();

        async Task<bool> RecordAsync(Func<ITableSessionOrderLedger, Task> record)
        {
            await using (var scope = database.NewScope(tenant))
            {
                await record(scope.ServiceProvider.GetRequiredService<ITableSessionOrderLedger>());
            }

            return await InScopeAsync(tenant, services => services.GetRequiredService<ITableSessionOrdersChecker>().HasOpenOrdersAsync(tenant, session));
        }

        Assert.True(await RecordAsync(ledger => ledger.RecordOpenedAsync(tenant, first, session)));
        Assert.True(await RecordAsync(ledger => ledger.RecordOpenedAsync(tenant, first, session)));
        Assert.False(await RecordAsync(ledger => ledger.RecordClosedAsync(tenant, first)));
        Assert.False(await RecordAsync(ledger => ledger.RecordClosedAsync(tenant, late)));
        Assert.False(await RecordAsync(ledger => ledger.RecordOpenedAsync(tenant, late, session)));
        Assert.False(await InScopeAsync(TenantId.New(), services => services.GetRequiredService<ITableSessionOrdersChecker>().HasOpenOrdersAsync(tenant, session)));
    }
}

using Locations.Application;
using Locations.Application.Abstractions;
using Locations.Domain.Locations;
using Locations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Locations.Infrastructure;

public sealed class LocationPersistenceTests(LocationsDatabase database) : IClassFixture<LocationsDatabase>
{
    private static readonly DateOnly Christmas = new(2026, 12, 25);
    private static readonly DateOnly NewYearsEve = new(2026, 12, 31);

    private RestaurantLocation NewLocation(TenantId tenantId, string name = "Cotonou Centre") => RestaurantLocation.Create(
        tenantId,
        LocationName.Create(name),
        Address.Create("Rue 12.345", "Cotonou", null, "BJ", 6.3703m, 2.3912m),
        BusinessHours.Create(
            new WeeklySlot(DayOfWeek.Monday, TimeRange.SameDay(new TimeOnly(11, 0), new TimeOnly(15, 0))),
            new WeeklySlot(DayOfWeek.Friday, TimeRange.Overnight(new TimeOnly(18, 0), new TimeOnly(2, 0)))),
        Capacity.Create(40),
        TimeZoneId.Create("Africa/Porto-Novo"),
        database.Clock);

    private async Task<RestaurantLocation> SaveAsync(RestaurantLocation location)
    {
        await using var scope = database.NewScope(location.TenantId);
        scope.ServiceProvider.GetRequiredService<ILocationRepository>().Add(location);
        await scope.ServiceProvider.GetRequiredService<ILocationsUnitOfWork>().SaveChangesAsync();
        return location;
    }

    private async Task<RestaurantLocation?> LoadAsync(TenantId requestTenant, TenantId askedTenant, LocationId id)
    {
        await using var scope = database.NewScope(requestTenant);
        return await scope.ServiceProvider.GetRequiredService<ILocationRepository>().FindAsync(askedTenant, id);
    }

    [Fact]
    public async Task A_location_comes_back_whole_with_its_value_objects_and_exceptions()
    {
        var tenant = TenantId.New();
        var location = NewLocation(tenant);
        location.Activate(database.Clock);
        location.AddExceptionalClosure(Christmas, database.Clock, "Noël");
        location.AddExceptionalSchedule(
            NewYearsEve, [TimeRange.Overnight(new TimeOnly(20, 0), new TimeOnly(4, 0))], database.Clock);
        await SaveAsync(location);

        var loaded = await LoadAsync(tenant, tenant, location.Id);

        Assert.NotNull(loaded);
        Assert.Equal(location.Name, loaded.Name);
        Assert.Equal(location.Address, loaded.Address);
        Assert.Equal(location.Hours, loaded.Hours);
        Assert.Equal(location.Capacity, loaded.Capacity);
        Assert.Equal(location.TimeZone, loaded.TimeZone);
        Assert.Equal(LocationStatus.Active, loaded.Status);
        Assert.Equal(location.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(2, loaded.ExceptionalSchedules.Count);
        Assert.True(loaded.ExceptionalSchedules.Single(schedule => schedule.Date == Christmas).IsClosed);
        Assert.Equal(
            TimeRange.Overnight(new TimeOnly(20, 0), new TimeOnly(4, 0)),
            Assert.Single(loaded.ExceptionalSchedules.Single(schedule => schedule.Date == NewYearsEve).Ranges));
        // Le domaine répond de même sur l'agrégat chargé.
        Assert.True(loaded.IsOpenAt(new DateOnly(2027, 1, 1), new TimeOnly(3, 0)));
        Assert.False(loaded.IsOpenAt(Christmas, new TimeOnly(19, 0)));
    }

    [Fact]
    public async Task A_location_of_another_tenant_cannot_be_loaded_even_with_its_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var ofA = await SaveAsync(NewLocation(tenantA));

        // Demander l'établissement de A en agissant pour B : le filtre du repository ET le query filter refusent.
        Assert.Null(await LoadAsync(tenantB, tenantB, ofA.Id));
        Assert.Null(await LoadAsync(tenantB, tenantA, ofA.Id));
        // Sans restaurant sélectionné, rien n'est visible.
        await using (var scope = database.NewScope(tenantId: null))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<LocationsDbContext>().Locations.ToListAsync());
        }

        Assert.NotNull(await LoadAsync(tenantA, tenantA, ofA.Id));
    }

    [Fact]
    public async Task Listing_stays_within_the_tenant_and_pages_by_name()
    {
        var tenant = TenantId.New();
        await SaveAsync(NewLocation(tenant, "Porto-Novo"));
        await SaveAsync(NewLocation(tenant, "Abomey"));
        await SaveAsync(NewLocation(tenant, "Cotonou"));
        await SaveAsync(NewLocation(TenantId.New(), "Ailleurs"));

        await using var scope = database.NewScope(tenant);
        var repository = scope.ServiceProvider.GetRequiredService<ILocationRepository>();
        var first = await repository.ListAsync(tenant, includeArchived: false, skip: 0, take: 2);
        var second = await repository.ListAsync(tenant, includeArchived: false, skip: 2, take: 2);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { "Abomey", "Cotonou" }, first.Items.Select(location => location.Name.Value));
        Assert.Equal("Porto-Novo", Assert.Single(second.Items).Name.Value);
    }

    [Fact]
    public async Task Changes_and_removed_exceptions_are_saved_with_their_events_in_the_outbox()
    {
        var tenant = TenantId.New();
        var location = NewLocation(tenant);
        location.AddExceptionalClosure(Christmas, database.Clock);
        await SaveAsync(location);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<ILocationRepository>().FindAsync(tenant, location.Id);
            loaded!.Rename(LocationName.Create("Cotonou Plage"), database.Clock);
            loaded.SetBusinessHours(BusinessHours.AlwaysClosed, database.Clock);
            loaded.RemoveExceptionalSchedule(Christmas, database.Clock);
            await scope.ServiceProvider.GetRequiredService<ILocationsUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await LoadAsync(tenant, tenant, location.Id);
        Assert.Equal("Cotonou Plage", reloaded!.Name.Value);
        Assert.True(reloaded.Hours.IsEmpty);
        Assert.Empty(reloaded.ExceptionalSchedules);

        await using var check = database.NewScope(tenant);
        var types = await check.ServiceProvider.GetRequiredService<LocationsDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync();
        Assert.Contains(nameof(LocationRenamed), types);
        Assert.Contains(nameof(BusinessHoursChanged), types);
        Assert.Contains(nameof(ExceptionalScheduleRemoved), types);
    }

    [Fact]
    public async Task Two_requests_adding_an_exception_for_the_same_date_cannot_both_win()
    {
        var tenant = TenantId.New();
        var location = await SaveAsync(NewLocation(tenant));

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<ILocationRepository>().FindAsync(tenant, location.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<ILocationRepository>().FindAsync(tenant, location.Id);
        mine!.AddExceptionalClosure(Christmas, database.Clock);
        theirs!.AddExceptionalClosure(Christmas, database.Clock, "Autre décision");

        await first.ServiceProvider.GetRequiredService<ILocationsUnitOfWork>().SaveChangesAsync();
        var lost = await Record.ExceptionAsync(
            () => second.ServiceProvider.GetRequiredService<ILocationsUnitOfWork>().SaveChangesAsync());

        // La version de l'établissement ou l'index unique arrête la seconde.
        Assert.True(lost is ConcurrencyException or DomainException, lost?.ToString());
        var stored = await LoadAsync(tenant, tenant, location.Id);
        Assert.Null(Assert.Single(stored!.ExceptionalSchedules).Reason);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Infrastructure;

public sealed class SchedulePersistenceTests(StaffDatabase database) : IClassFixture<StaffDatabase>
{
    private static readonly DateOnly Monday = new(2026, 1, 19);

    private async Task SaveAsync(Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope();
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task A_week_keeps_its_draft_and_published_shifts_apart_and_is_found_by_location_and_monday()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var member = StaffMemberId.New();
        var week = ScheduleWeek.Create(tenant, location, Monday, database.Clock);
        var night = week.AddShift(new ShiftSpec(member, Monday.AddDays(4), new TimeOnly(19, 0), new TimeOnly(1, 30), 30, "Bar", "Fermeture"), database.Clock);
        week.Publish(database.Clock);
        week.UpdateShift(night.Id, new ShiftSpec(member, Monday.AddDays(4), new TimeOnly(20, 0), new TimeOnly(1, 30), 30, "Bar", null), database.Clock);
        await SaveAsync(services => services.GetRequiredService<IScheduleRepository>().Add(week));

        await using var read = database.NewScope();
        var schedules = read.ServiceProvider.GetRequiredService<IScheduleRepository>();
        var loaded = await schedules.FindAsync(tenant, location, Monday);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.PublishedVersion);
        Assert.Equal(new TimeOnly(20, 0), Assert.Single(loaded.Shifts).Start);
        var published = Assert.Single(loaded.PublishedShifts);
        Assert.Equal(new TimeOnly(19, 0), published.Start);
        Assert.Equal("Fermeture", published.Note);
        Assert.True(published.EndsNextDay);
        Assert.Equal(member, published.StaffMemberId);
        Assert.True(loaded.HasUnpublishedChanges);
        Assert.Null(await schedules.FindAsync(TenantId.New(), location, Monday));
        Assert.Single(await schedules.ListAsync(tenant, null, Monday.AddDays(-7), Monday));
    }

    [Fact]
    public async Task A_second_week_for_the_same_location_and_monday_is_a_conflict()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        await SaveAsync(services => services.GetRequiredService<IScheduleRepository>().Add(ScheduleWeek.Create(tenant, location, Monday, database.Clock)));

        var conflict = await Assert.ThrowsAsync<DomainException>(() =>
            SaveAsync(services => services.GetRequiredService<IScheduleRepository>().Add(ScheduleWeek.Create(tenant, location, Monday, database.Clock))));

        Assert.Equal("staff.concurrent_update", conflict.Code);
    }

    [Fact]
    public async Task Settings_absences_and_swaps_come_back()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var awa = StaffMemberId.New();
        var koffi = StaffMemberId.New();
        var settings = ScheduleSettings.Default(tenant, location);
        settings.Update(["Salle", "Pizza"], 39);
        var absence = TimeOffRequest.Request(tenant, awa, Monday, Monday.AddDays(2), TimeOffKind.Leave, "Mariage", false, Monday.AddDays(-3), database.Clock);
        var week = ScheduleWeek.Create(tenant, location, Monday, database.Clock);
        var shift = week.AddShift(new ShiftSpec(awa, Monday.AddDays(5), new TimeOnly(10, 0), new TimeOnly(14, 0), 0, null, null), database.Clock);
        week.Publish(database.Clock);
        var swap = ShiftSwapRequest.Request(week, shift.Id, awa, koffi, database.Clock.UtcNow.AddDays(10), null, database.Clock);
        await SaveAsync(services =>
        {
            var schedules = services.GetRequiredService<IScheduleRepository>();
            schedules.AddSettings(settings);
            schedules.Add(week);
            services.GetRequiredService<ITimeOffRepository>().Add(absence);
            services.GetRequiredService<IShiftSwapRepository>().Add(swap);
        });

        await using var read = database.NewScope();
        var loadedSettings = await read.ServiceProvider.GetRequiredService<IScheduleRepository>().FindSettingsAsync(tenant, location);
        Assert.Equal(["Salle", "Pizza"], loadedSettings!.Positions);
        Assert.Equal(39, loadedSettings.WeeklyHoursLimit);

        var absences = await read.ServiceProvider.GetRequiredService<ITimeOffRepository>().ListAsync(tenant, Monday.AddDays(2), Monday.AddDays(9), awa, TimeOffStatus.Pending, 10);
        Assert.Equal("Mariage", Assert.Single(absences).Comment);
        Assert.Empty(await read.ServiceProvider.GetRequiredService<ITimeOffRepository>().ListAsync(tenant, Monday.AddDays(3), Monday.AddDays(9), null, null, 10));

        var swaps = read.ServiceProvider.GetRequiredService<IShiftSwapRepository>();
        Assert.Equal(swap.Id, (await swaps.FindOpenForShiftAsync(tenant, shift.Id))!.Id);
        Assert.Single(await swaps.ListAsync(tenant, database.Clock.UtcNow, koffi, openOnly: true, 10));
        Assert.Empty(await swaps.ListAsync(tenant, database.Clock.UtcNow, StaffMemberId.New(), openOnly: false, 10));
    }
}

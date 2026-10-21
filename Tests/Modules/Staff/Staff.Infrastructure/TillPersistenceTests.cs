using Microsoft.Extensions.DependencyInjection;
using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Infrastructure;

public sealed class TillPersistenceTests(StaffDatabase database) : IClassFixture<StaffDatabase>
{
    private static readonly PermissionCatalog Catalog = PermissionCatalog.Of("staff.manage");

    private async Task<StaffMember> GivenMemberAsync(TenantId tenant, LocationId location)
    {
        var owner = Role.CreateSystem(tenant, "Owner", Catalog.Codes, Catalog, isNameUnique: true, database.Clock);
        var member = StaffMember.CreateOwner(tenant, UserId.New(), owner.ToReference(), isUserUniqueInTenant: true, database.Clock);
        member.SetPin("v1.1.c2FsdA==.aGFzaA==");
        await using var scope = database.NewScope();
        scope.ServiceProvider.GetRequiredService<IRoleRepository>().Add(owner);
        scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>().Add(member);
        await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task A_till_and_a_pin_come_back_and_the_till_is_found_by_its_id_alone()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var member = await GivenMemberAsync(tenant, location);
        var till = Till.Register(new LocationReference(tenant, location, true), "Comptoir", 5, "ABCDEF", database.Clock);
        await using (var scope = database.NewScope())
        {
            scope.ServiceProvider.GetRequiredService<ITillRepository>().Add(till);
            await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
        }

        await using var read = database.NewScope();
        var loaded = await read.ServiceProvider.GetRequiredService<ITillRepository>().FindAsync(till.Id);
        var loadedMember = await read.ServiceProvider.GetRequiredService<IStaffMemberRepository>().FindAsync(tenant, member.Id);

        Assert.NotNull(loaded);
        Assert.Equal((tenant, location, "Comptoir", 5, "ABCDEF"), (loaded.TenantId, loaded.LocationId, loaded.Name, loaded.AutoLockMinutes, loaded.SecretHash));
        Assert.True(loadedMember!.HasPin);
        Assert.Single(await read.ServiceProvider.GetRequiredService<ITillRepository>().ListAsync(tenant));
        Assert.Empty(await read.ServiceProvider.GetRequiredService<ITillRepository>().ListAsync(TenantId.New()));
    }

    [Fact]
    public async Task A_member_has_one_open_attendance_at_most()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var member = await GivenMemberAsync(tenant, location);
        await using (var scope = database.NewScope())
        {
            scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>().Add(TimeEntry.ClockIn(member, location, database.Clock));
            await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
        }

        await using (var second = database.NewScope())
        {
            second.ServiceProvider.GetRequiredService<ITimeEntryRepository>().Add(TimeEntry.ClockIn(member, location, database.Clock));
            var refused = await Assert.ThrowsAsync<DomainException>(() => second.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync());
            Assert.Equal(TimeEntryErrors.AlreadyOpen, refused.Error);
        }

        await using var read = database.NewScope();
        var entries = read.ServiceProvider.GetRequiredService<ITimeEntryRepository>();
        var open = await entries.FindOpenAsync(tenant, member.Id);
        Assert.NotNull(open);
        Assert.Single(await entries.ListOpenAsync(tenant, [member.Id]));
        Assert.Single(await entries.ListAsync(tenant, database.Clock.UtcNow.AddHours(-1), database.Clock.UtcNow.AddHours(1), location, member.Id, 10));
        Assert.Empty(await entries.ListAsync(tenant, database.Clock.UtcNow.AddHours(-1), database.Clock.UtcNow.AddHours(1), LocationId.New(), null, 10));
    }
}

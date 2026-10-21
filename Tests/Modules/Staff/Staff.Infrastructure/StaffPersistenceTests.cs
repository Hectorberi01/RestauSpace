using Microsoft.Extensions.DependencyInjection;
using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Infrastructure;

public sealed class StaffPersistenceTests(StaffDatabase database) : IClassFixture<StaffDatabase>
{
    private static readonly PermissionCatalog Catalog = PermissionCatalog.Of("locations.manage", "staff.manage", "roles.manage");

    private async Task SaveAsync(Action<IStaffMemberRepository, IRoleRepository> change)
    {
        await using var scope = database.NewScope();
        change(
            scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>(),
            scope.ServiceProvider.GetRequiredService<IRoleRepository>());
        await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
    }

    private Role OwnerRole(TenantId tenantId) =>
        Role.CreateSystem(tenantId, "Owner", Catalog.Codes, Catalog, isNameUnique: true, database.Clock);

    [Fact]
    public async Task A_member_and_a_role_come_back_whole_with_assignments_and_permissions()
    {
        var tenant = TenantId.New();
        var owner = OwnerRole(tenant);
        var member = StaffMember.CreateOwner(tenant, UserId.New(), owner.ToReference(), isUserUniqueInTenant: true, database.Clock);
        var location = LocationId.New();
        member.AssignToLocation(new LocationReference(tenant, location, IsOperational: true), owner.ToReference(), database.Clock);
        await SaveAsync((members, roles) =>
        {
            roles.Add(owner);
            members.Add(member);
        });

        await using var scope = database.NewScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>().FindAsync(tenant, member.Id);
        var loadedRole = await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindSystemRoleAsync(tenant, "Owner");

        Assert.NotNull(loaded);
        Assert.Equal(StaffStatus.Active, loaded.Status);
        Assert.Equal(owner.Id, loaded.EffectiveTenantRole);
        Assert.Equal(owner.Id, loaded.EffectiveRoleAt(location));
        Assert.NotNull(loadedRole);
        Assert.True(loadedRole.IsSystem);
        Assert.Equal(3, loadedRole.Permissions.Count);
        Assert.True(loadedRole.HasPermission(PermissionCode.Create("staff.manage")));
    }

    [Fact]
    public async Task A_role_held_in_a_location_is_seen_held_and_a_deleted_role_takes_its_permissions_along()
    {
        var tenant = TenantId.New();
        var owner = OwnerRole(tenant);
        var waiter = Role.Create(tenant, "Serveur", isNameUnique: true, database.Clock);
        waiter.Grant(PermissionCode.Create("locations.manage"), Catalog, database.Clock);
        var member = StaffMember.CreateOwner(tenant, UserId.New(), owner.ToReference(), isUserUniqueInTenant: true, database.Clock);
        member.AssignToLocation(new LocationReference(tenant, LocationId.New(), IsOperational: true), waiter.ToReference(), database.Clock);
        await SaveAsync((members, roles) =>
        {
            roles.Add(owner);
            roles.Add(waiter);
            members.Add(member);
        });

        await using (var scope = database.NewScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
            Assert.True(await members.IsRoleHeldAsync(tenant, waiter.Id));
            Assert.True(await members.IsRoleHeldAsync(tenant, owner.Id));
            Assert.False(await members.IsRoleHeldAsync(TenantId.New(), waiter.Id));
        }

        await using (var scope = database.NewScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var loaded = await roles.FindAsync(tenant, waiter.Id);
            loaded!.Delete(isHeld: false, database.Clock);
            roles.Remove(loaded);
            await scope.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindAsync(tenant, waiter.Id));
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IRoleRepository>().ListAsync(tenant));
        }
    }

    [Fact]
    public async Task System_roles_created_together_from_the_same_catalog_each_keep_all_their_permissions()
    {
        var tenant = TenantId.New();
        var owner = OwnerRole(tenant);
        var manager = Role.CreateSystem(
            tenant,
            "Manager",
            Catalog.Codes.Where(code => code.Value != "staff.manage" && code.Value != "roles.manage"),
            Catalog,
            isNameUnique: true,
            database.Clock);
        await SaveAsync((_, roles) =>
        {
            roles.Add(owner);
            roles.Add(manager);
        });

        await using var scope = database.NewScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var loadedOwner = await roles.FindSystemRoleAsync(tenant, "Owner");
        var loadedManager = await roles.FindSystemRoleAsync(tenant, "Manager");

        Assert.NotNull(loadedOwner);
        Assert.NotNull(loadedManager);
        Assert.Equal(3, loadedOwner.Permissions.Count);
        Assert.True(loadedOwner.HasPermission(PermissionCode.Create("locations.manage")));
        Assert.Single(loadedManager.Permissions);
        Assert.True(loadedManager.HasPermission(PermissionCode.Create("locations.manage")));
    }

    [Fact]
    public async Task Queries_never_cross_the_tenant_they_are_given()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var role = OwnerRole(tenantA);
        var member = StaffMember.Invite(tenantA, UserId.New(), isUserUniqueInTenant: true, database.Clock);
        await SaveAsync((members, roles) =>
        {
            roles.Add(role);
            members.Add(member);
        });

        await using var scope = database.NewScope();
        var members = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        Assert.Null(await members.FindAsync(tenantB, member.Id));
        Assert.Null(await members.FindByUserAsync(tenantB, member.UserId));
        Assert.Null(await roles.FindAsync(tenantB, role.Id));
        Assert.Empty(await roles.ListAsync(tenantB));
        Assert.NotNull(await members.FindByUserAsync(tenantA, member.UserId));
        Assert.Single(await members.ListByUserAsync(member.UserId));
    }

    [Fact]
    public async Task The_database_refuses_a_second_profile_for_the_same_user_and_a_role_name_differing_only_by_case()
    {
        var tenant = TenantId.New();
        var userId = UserId.New();
        await SaveAsync((members, roles) =>
        {
            members.Add(StaffMember.Invite(tenant, userId, isUserUniqueInTenant: true, database.Clock));
            roles.Add(Role.Create(tenant, "Serveur", isNameUnique: true, database.Clock));
        });

        // Les deux vérifications ont été « perdues » par une requête concurrente : les index sont le dernier rempart.
        var duplicateMember = await Assert.ThrowsAsync<DomainException>(() => SaveAsync((members, _) =>
            members.Add(StaffMember.Invite(tenant, userId, isUserUniqueInTenant: true, database.Clock))));
        var duplicateRole = await Assert.ThrowsAsync<DomainException>(() => SaveAsync((_, roles) =>
            roles.Add(Role.Create(tenant, "SERVEUR", isNameUnique: true, database.Clock))));

        Assert.Equal(StaffMemberErrors.UserAlreadyStaffInTenant, duplicateMember.Error);
        Assert.Equal(RoleErrors.NameAlreadyUsed, duplicateRole.Error);
    }

    [Fact]
    public async Task Two_owners_removing_each_other_at_once_cannot_both_succeed()
    {
        var tenant = TenantId.New();
        var owner = OwnerRole(tenant);
        var first = StaffMember.CreateOwner(tenant, UserId.New(), owner.ToReference(), isUserUniqueInTenant: true, database.Clock);
        var second = StaffMember.CreateOwner(tenant, UserId.New(), owner.ToReference(), isUserUniqueInTenant: true, database.Clock);
        await SaveAsync((members, roles) =>
        {
            roles.Add(owner);
            members.Add(first);
            members.Add(second);
        });

        await using var one = database.NewScope();
        await using var other = database.NewScope();
        async Task<StaffMember> PrepareLeaveAsync(AsyncServiceScope scope, StaffMember leaving)
        {
            var members = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var loaded = (await members.FindAsync(tenant, leaving.Id))!;
            Assert.Equal(1, await members.CountActiveTenantWideHoldersAsync(tenant, owner.Id, loaded.Id));
            roles.GuardDecisionsOn((await roles.FindAsync(tenant, owner.Id))!);
            loaded.Leave(database.Clock);
            return loaded;
        }

        await PrepareLeaveAsync(one, first);
        await PrepareLeaveAsync(other, second);
        await one.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync();
        var lost = await Record.ExceptionAsync(() => other.ServiceProvider.GetRequiredService<IStaffUnitOfWork>().SaveChangesAsync());

        Assert.IsType<ConcurrencyException>(lost);
        await using var check = database.NewScope();
        Assert.Equal(
            1,
            await check.ServiceProvider.GetRequiredService<IStaffMemberRepository>()
                .CountActiveTenantWideHoldersAsync(tenant, owner.Id, StaffMemberId.New()));
    }
}

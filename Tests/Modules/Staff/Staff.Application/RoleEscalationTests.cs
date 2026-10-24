using Staff.Application.Commands;
using Staff.Domain;

namespace Staff.Application;

/// <summary>Jamais plus que ses propres droits : rôles donnés, permissions ajoutées, son propre rôle.</summary>
public sealed class RoleEscalationTests
{
    private readonly StaffFixture _fixture = new();

    private Guid _ownerId;

    /// <summary>Un membre actif non propriétaire avec un rôle tenant-wide de ces permissions ; la requête agit ensuite en son nom.</summary>
    private async Task<Guid> GivenActingManagerAsync(params string[] permissions)
    {
        _ownerId = await _fixture.GivenOnboardedTenantAsync();
        var roleId = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("RH"));
        foreach (var permission in permissions)
        {
            await _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, permission));
        }

        var managerId = await _fixture.Invite.HandleAsync(new InviteStaffCommand("rh@example.com", roleId));
        await _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(_fixture.Account("rh@example.com").Value, _fixture.TenantA.Value));
        _fixture.ActingUser = _fixture.Account("rh@example.com");
        return managerId;
    }

    private Guid OwnerRole => _fixture.SystemRole(_fixture.TenantA, SystemRoles.Owner).Id.Value;

    [Fact]
    public async Task Only_an_owner_gives_the_owner_role()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff, StaffPermissions.ManageRoles);
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));
        var here = _fixture.GivenLocation(_fixture.TenantA);

        foreach (var attempt in new Func<Task>[]
        {
            () => _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(waiter, OwnerRole)),
            () => _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(waiter, here.Value, OwnerRole)),
            () => _fixture.Invite.HandleAsync(new InviteStaffCommand("boss@example.com", OwnerRole)),
        })
        {
            var refused = await Assert.ThrowsAsync<ForbiddenException>(attempt);
            Assert.Equal(StaffApplicationErrors.OwnerRoleReserved, refused.Error);
        }

        Assert.Null(_fixture.Member(waiter).TenantWideRoleId);
        Assert.Empty(_fixture.Member(waiter).Assignments);
        Assert.DoesNotContain("boss@example.com", _fixture.InvitedAccounts);
    }

    [Fact]
    public async Task A_role_with_a_permission_the_caller_lacks_cannot_be_given()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        var planner = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Planning"));
        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(planner, StaffPermissions.Schedule));
        var manager = _fixture.SystemRole(_fixture.TenantA, SystemRoles.Manager).Id.Value;
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));
        _fixture.ActingUser = _fixture.Account("rh@example.com");
        var here = _fixture.GivenLocation(_fixture.TenantA);

        foreach (var attempt in new Func<Task>[]
        {
            () => _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(waiter, planner)),
            () => _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(waiter, manager)),
            () => _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(waiter, here.Value, planner)),
            () => _fixture.Invite.HandleAsync(new InviteStaffCommand("cook@example.com", planner)),
        })
        {
            var refused = await Assert.ThrowsAsync<ForbiddenException>(attempt);
            Assert.Equal(StaffApplicationErrors.RoleNotHeld, refused.Error);
        }

        Assert.Null(_fixture.Member(waiter).TenantWideRoleId);
        Assert.Empty(_fixture.Member(waiter).Assignments);
    }

    [Fact]
    public async Task A_role_within_the_callers_permissions_can_be_given()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff, StaffPermissions.Schedule);
        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        var planner = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Planning"));
        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(planner, StaffPermissions.Schedule));
        _fixture.ActingUser = _fixture.Account("rh@example.com");
        var here = _fixture.GivenLocation(_fixture.TenantA);

        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com", planner));
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(waiter, here.Value, planner));

        Assert.Equal(RoleId.From(planner), _fixture.Member(waiter).TenantWideRoleId);
        Assert.Equal(RoleId.From(planner), Assert.Single(_fixture.Member(waiter).Assignments).RoleId);
    }

    [Fact]
    public async Task An_owner_can_make_another_member_owner()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));

        await _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(waiter, OwnerRole));

        Assert.Equal(RoleId.From(OwnerRole), _fixture.Member(waiter).TenantWideRoleId);
    }

    [Fact]
    public async Task Nobody_changes_their_own_role_or_assignments()
    {
        var ownerId = await _fixture.GivenOnboardedTenantAsync();
        await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));
        var waiterRole = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));
        var here = _fixture.GivenLocation(_fixture.TenantA);
        _fixture.ActingUser = null;
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(ownerId, here.Value, waiterRole));
        _fixture.ActingUser = _fixture.Member(ownerId).UserId;
        var elsewhere = _fixture.GivenLocation(_fixture.TenantA);
        var saves = _fixture.SaveCount;

        foreach (var attempt in new Func<Task>[]
        {
            () => _fixture.RemoveTenantRole.HandleAsync(new RemoveTenantWideRoleCommand(ownerId)),
            () => _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(ownerId, waiterRole)),
            () => _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(ownerId, elsewhere.Value, waiterRole)),
            () => _fixture.RemoveFromLocation.HandleAsync(new RemoveStaffFromLocationCommand(ownerId, here.Value)),
        })
        {
            var refused = await Assert.ThrowsAsync<ForbiddenException>(attempt);
            Assert.Equal(StaffApplicationErrors.CannotChangeOwnRole, refused.Error);
        }

        Assert.Equal(RoleId.From(OwnerRole), _fixture.Member(ownerId).TenantWideRoleId);
        Assert.Single(_fixture.Member(ownerId).Assignments);
        Assert.Equal(saves, _fixture.SaveCount);
    }

    [Fact]
    public async Task Only_an_owner_takes_away_the_owner_role()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        _fixture.ActingUser = null;
        var second = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));
        _fixture.ActingUser = _fixture.Account("rh@example.com");

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.RemoveTenantRole.HandleAsync(new RemoveTenantWideRoleCommand(second)));

        Assert.Equal(StaffApplicationErrors.OwnerRoleReserved, refused.Error);
        Assert.Equal(RoleId.From(OwnerRole), _fixture.Member(second).TenantWideRoleId);
    }

    [Fact]
    public async Task Only_held_permissions_are_granted_and_revoking_stays_free()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageRoles, StaffPermissions.Schedule);
        var roleId = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Chef de rang"));

        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, StaffPermissions.Schedule));
        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, StaffPermissions.ManageStaff)));
        var unknown = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, "orders.cancel")));

        Assert.Equal(StaffApplicationErrors.PermissionNotHeld, refused.Error);
        Assert.Equal(RoleErrors.PermissionUnknown, unknown.Error);
        var role = _fixture.ListRoles(_fixture.TenantA).Single(r => r.Id.Value == roleId);
        Assert.Equal(new[] { StaffPermissions.Schedule }, role.Permissions.Select(permission => permission.Value));

        // Le rôle de l'appelant lui-même : retirer reste permis.
        var mine = _fixture.ListRoles(_fixture.TenantA).Single(r => r.Name.Value == "RH").Id.Value;
        await _fixture.Revoke.HandleAsync(new RevokePermissionCommand(mine, StaffPermissions.Schedule));
        await _fixture.Revoke.HandleAsync(new RevokePermissionCommand(roleId, StaffPermissions.Schedule));
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public async Task A_suspended_caller_holds_nothing_to_give()
    {
        var managerId = await GivenActingManagerAsync(StaffPermissions.ManageStaff, StaffPermissions.Schedule);
        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        var planner = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Planning"));
        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(planner, StaffPermissions.Schedule));
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));
        await _fixture.Suspend.HandleAsync(new SuspendStaffCommand(managerId));
        _fixture.ActingUser = _fixture.Account("rh@example.com");

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.AssignTenantRole.HandleAsync(new AssignTenantWideRoleCommand(waiter, planner)));

        Assert.Equal(StaffApplicationErrors.RoleNotHeld, refused.Error);
    }

    [Fact]
    public async Task Only_an_owner_suspends_reinstates_or_removes_an_owner()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        _fixture.ActingUser = null;
        var second = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));
        _fixture.ActingUser = _fixture.Account("rh@example.com");

        foreach (var attempt in new Func<Task>[]
        {
            () => _fixture.Suspend.HandleAsync(new SuspendStaffCommand(second)),
            () => _fixture.Remove.HandleAsync(new RemoveStaffCommand(second)),
        })
        {
            var refused = await Assert.ThrowsAsync<ForbiddenException>(attempt);
            Assert.Equal(StaffApplicationErrors.OwnerRoleReserved, refused.Error);
        }

        Assert.Equal(StaffStatus.Active, _fixture.Member(second).Status);

        // Un Owner suspendu ne peut être réactivé que par un Owner.
        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        await _fixture.Suspend.HandleAsync(new SuspendStaffCommand(second));
        _fixture.ActingUser = _fixture.Account("rh@example.com");
        var reinstate = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Reinstate.HandleAsync(new ReinstateStaffCommand(second)));
        Assert.Equal(StaffApplicationErrors.OwnerRoleReserved, reinstate.Error);

        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        await _fixture.Reinstate.HandleAsync(new ReinstateStaffCommand(second));
        Assert.Equal(StaffStatus.Active, _fixture.Member(second).Status);
    }

    [Fact]
    public async Task An_owner_at_a_location_is_also_reserved_to_owners()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        _fixture.ActingUser = _fixture.Member(_ownerId).UserId;
        var cook = await _fixture.Invite.HandleAsync(new InviteStaffCommand("cook@example.com"));
        var here = _fixture.GivenLocation(_fixture.TenantA);
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(cook, here.Value, OwnerRole));
        _fixture.ActingUser = _fixture.Account("rh@example.com");

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Remove.HandleAsync(new RemoveStaffCommand(cook)));

        Assert.Equal(StaffApplicationErrors.OwnerRoleReserved, refused.Error);
        Assert.Equal(StaffStatus.Invited, _fixture.Member(cook).Status);
    }

    [Fact]
    public async Task A_non_owner_manages_the_status_of_other_non_owners()
    {
        await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));
        await _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(_fixture.Account("waiter@example.com").Value, _fixture.TenantA.Value));

        await _fixture.Suspend.HandleAsync(new SuspendStaffCommand(waiter));
        await _fixture.Reinstate.HandleAsync(new ReinstateStaffCommand(waiter));
        await _fixture.Remove.HandleAsync(new RemoveStaffCommand(waiter));

        Assert.Equal(StaffStatus.Left, _fixture.Member(waiter).Status);
    }

    [Fact]
    public async Task Nobody_suspends_reinstates_or_removes_themselves()
    {
        var managerId = await GivenActingManagerAsync(StaffPermissions.ManageStaff);
        var ownerId = _ownerId;
        _fixture.ActingUser = null;
        await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));

        foreach (var (member, user) in new[] { (managerId, _fixture.Account("rh@example.com")), (ownerId, _fixture.Member(ownerId).UserId) })
        {
            _fixture.ActingUser = user;
            foreach (var attempt in new Func<Task>[]
            {
                () => _fixture.Suspend.HandleAsync(new SuspendStaffCommand(member)),
                () => _fixture.Reinstate.HandleAsync(new ReinstateStaffCommand(member)),
                () => _fixture.Remove.HandleAsync(new RemoveStaffCommand(member)),
            })
            {
                var refused = await Assert.ThrowsAsync<ForbiddenException>(attempt);
                Assert.Equal(StaffApplicationErrors.CannotChangeOwnRole, refused.Error);
            }

            Assert.Equal(StaffStatus.Active, _fixture.Member(member).Status);
        }
    }

    [Fact]
    public async Task Without_a_caller_the_platform_paths_are_not_checked()
    {
        await _fixture.GivenOnboardedTenantAsync();
        _fixture.ActingUser = null;
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com", OwnerRole));
        var roleId = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));

        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, StaffPermissions.ManageStaff));

        Assert.Equal(RoleId.From(OwnerRole), _fixture.Member(waiter).TenantWideRoleId);
    }
}

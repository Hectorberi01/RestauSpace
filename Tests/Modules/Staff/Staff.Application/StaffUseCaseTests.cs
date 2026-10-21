using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Application.Queries;
using Staff.Domain;

namespace Staff.Application;

public sealed class StaffUseCaseTests
{
    private readonly StaffFixture _fixture = new();

    [Fact]
    public async Task Onboarding_creates_the_system_roles_and_an_active_owner_holding_every_permission()
    {
        var ownerId = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "owner@example.com"));

        var owner = _fixture.Member(ownerId);
        var ownerRole = _fixture.SystemRole(_fixture.TenantA, SystemRoles.Owner);
        var managerRole = _fixture.SystemRole(_fixture.TenantA, SystemRoles.Manager);
        Assert.Equal(StaffStatus.Active, owner.Status);
        Assert.Equal(ownerRole.Id, owner.EffectiveTenantRole);
        Assert.Equal(_fixture.Account("owner@example.com"), owner.UserId);
        Assert.Equal(new[] { "owner@example.com" }, _fixture.InvitedAccounts);
        Assert.Equal(
            new[] { StaffFixture.LocationsManage, StaffPermissions.ManageRoles, StaffPermissions.ManageStaff, StaffPermissions.Schedule },
            ownerRole.Permissions.Select(permission => permission.Value).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { StaffFixture.LocationsManage, StaffPermissions.Schedule },
            managerRole.Permissions.Select(permission => permission.Value).Order(StringComparer.Ordinal));
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_second_owner_reuses_the_system_roles_and_an_existing_account()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var existing = UserId.New();
        _fixture.Accounts["second@example.com"] = existing;

        var secondId = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));

        Assert.Equal(existing, _fixture.Member(secondId).UserId);
        Assert.DoesNotContain("second@example.com", _fixture.InvitedAccounts);
        Assert.Single(_fixture.ListRoles(_fixture.TenantA), role => role.Name.Value == SystemRoles.Owner);
    }

    [Fact]
    public async Task Onboarding_is_refused_for_an_unknown_or_closed_tenant_and_for_a_user_already_member()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(Guid.NewGuid(), "owner@example.com")));

        _fixture.Tenants[_fixture.TenantB] = TenantAccess.ReadOnly;
        var closed = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantB.Value, "owner@example.com")));
        Assert.Equal(StaffApplicationErrors.TenantNotWritable, closed.Error);

        await _fixture.GivenOnboardedTenantAsync();
        var twice = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "owner@example.com")));
        Assert.Equal(StaffMemberErrors.UserAlreadyStaffInTenant, twice.Error);
    }

    [Fact]
    public async Task An_invited_person_accepts_and_can_then_act_for_the_restaurant()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var manager = _fixture.SystemRole(_fixture.TenantA, SystemRoles.Manager);

        var invitedId = await _fixture.Invite.HandleAsync(new InviteStaffCommand("chef@example.com", manager.Id.Value));
        var chef = _fixture.Account("chef@example.com");
        Assert.False(await _fixture.IsActiveMember.HandleAsync(new IsActiveStaffMemberQuery(chef.Value, _fixture.TenantA.Value)));
        Assert.False(await _fixture.Checker.HasTenantPermissionAsync(chef, _fixture.TenantA, StaffFixture.LocationsManage));

        // L'invitation est acceptée depuis « mes restaurants », sans aucun restaurant sélectionné.
        _fixture.CurrentTenantId = null;
        var memberships = await _fixture.MyMemberships.HandleAsync(new ListMyMembershipsQuery(chef.Value));
        Assert.Equal(new MembershipDto(_fixture.TenantA.Value, StaffFixture.TenantName(_fixture.TenantA), invitedId, StaffStatus.Invited), Assert.Single(memberships));
        await _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(chef.Value, _fixture.TenantA.Value));

        Assert.Equal(StaffStatus.Active, _fixture.Member(invitedId).Status);
        Assert.True(await _fixture.IsActiveMember.HandleAsync(new IsActiveStaffMemberQuery(chef.Value, _fixture.TenantA.Value)));
        Assert.True(await _fixture.Checker.HasTenantPermissionAsync(chef, _fixture.TenantA, StaffFixture.LocationsManage));
        Assert.False(await _fixture.Checker.HasTenantPermissionAsync(chef, _fixture.TenantA, StaffPermissions.ManageStaff));
        Assert.False(await _fixture.Checker.HasTenantPermissionAsync(chef, _fixture.TenantB, StaffFixture.LocationsManage));
    }

    [Fact]
    public async Task Accepting_needs_an_invitation_of_that_restaurant()
    {
        await _fixture.GivenOnboardedTenantAsync();

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(Guid.NewGuid(), _fixture.TenantA.Value)));

        Assert.Equal(StaffApplicationErrors.InvitationNotFound, refused.Error);
    }

    [Fact]
    public async Task The_last_active_owner_cannot_be_suspended_removed_or_lose_the_owner_role()
    {
        var ownerId = await _fixture.GivenOnboardedTenantAsync();

        foreach (var attempt in new Func<Task>[]
        {
            () => _fixture.Suspend.HandleAsync(new SuspendStaffCommand(ownerId)),
            () => _fixture.Remove.HandleAsync(new RemoveStaffCommand(ownerId)),
            () => _fixture.RemoveTenantRole.HandleAsync(new RemoveTenantWideRoleCommand(ownerId)),
        })
        {
            var refused = await Assert.ThrowsAsync<DomainException>(attempt);
            Assert.Equal(StaffApplicationErrors.LastOwner, refused.Error);
        }

        Assert.Equal(StaffStatus.Active, _fixture.Member(ownerId).Status);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_restaurant_is_a_sole_ownership_until_a_second_owner_is_active()
    {
        var firstId = await _fixture.GivenOnboardedTenantAsync();
        var userId = _fixture.Member(firstId).UserId.Value;

        Assert.Equal([_fixture.TenantA.Value], await _fixture.SoleOwnerships.HandleAsync(new ListSoleOwnershipsQuery(userId)));

        await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));

        Assert.Empty(await _fixture.SoleOwnerships.HandleAsync(new ListSoleOwnershipsQuery(userId)));
        Assert.Empty(await _fixture.SoleOwnerships.HandleAsync(new ListSoleOwnershipsQuery(Guid.Empty)));
    }

    [Fact]
    public async Task With_a_second_active_owner_an_owner_can_leave_and_the_owner_role_guards_the_decision()
    {
        var firstId = await _fixture.GivenOnboardedTenantAsync();
        await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));

        await _fixture.Remove.HandleAsync(new RemoveStaffCommand(firstId));

        Assert.Equal(StaffStatus.Left, _fixture.Member(firstId).Status);
        Assert.Equal(SystemRoles.Owner, Assert.Single(_fixture.Guarded).Name.Value);
    }

    [Fact]
    public async Task Assigning_to_a_location_needs_a_location_and_a_role_of_the_same_restaurant()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var waiterRole = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));
        var memberId = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));
        var here = _fixture.GivenLocation(_fixture.TenantA);
        var elsewhere = _fixture.GivenLocation(_fixture.TenantB);

        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(memberId, here.Value, waiterRole));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(memberId, elsewhere.Value, waiterRole)));

        Assert.Equal(StaffApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Equal(RoleId.From(waiterRole), _fixture.Member(memberId).Assignments.Single().RoleId);
    }

    [Fact]
    public async Task The_plan_limits_invitations_and_a_member_who_left_frees_a_place()
    {
        _fixture.MaxStaffMembers = 2;
        await _fixture.GivenOnboardedTenantAsync();
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com"));

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Invite.HandleAsync(new InviteStaffCommand("cook@example.com")));
        await _fixture.Remove.HandleAsync(new RemoveStaffCommand(waiter));
        var cook = await _fixture.Invite.HandleAsync(new InviteStaffCommand("cook@example.com"));

        Assert.Equal(StaffApplicationErrors.PlanLimitReached, refused.Error);
        Assert.Equal(StaffStatus.Invited, _fixture.Member(cook).Status);
    }

    [Fact]
    public async Task The_platform_onboards_the_owner_whatever_the_plan_limit()
    {
        _fixture.MaxStaffMembers = 0;

        var owner = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantB.Value, "owner-b@example.com"));

        Assert.Equal(StaffStatus.Active, _fixture.Member(owner).Status);
    }

    [Fact]
    public async Task A_member_of_another_restaurant_is_not_found()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var ofB = await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantB.Value, "b@example.com"));

        var refused = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Suspend.HandleAsync(new SuspendStaffCommand(ofB)));

        Assert.Equal(StaffApplicationErrors.MemberNotFound, refused.Error);
        Assert.Equal(StaffStatus.Active, _fixture.Member(ofB).Status);
    }

    [Fact]
    public async Task Custom_roles_get_only_known_permissions_and_system_roles_stay_untouched()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var roleId = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Chef de rang"));

        await _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, StaffFixture.LocationsManage));
        var unknown = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Grant.HandleAsync(new GrantPermissionCommand(roleId, "orders.cancel")));
        var system = await Assert.ThrowsAsync<DomainException>(() => _fixture.Revoke.HandleAsync(
            new RevokePermissionCommand(_fixture.SystemRole(_fixture.TenantA, SystemRoles.Owner).Id.Value, StaffPermissions.ManageStaff)));
        var duplicate = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.CreateRole.HandleAsync(new CreateRoleCommand("CHEF DE RANG")));

        Assert.Equal(RoleErrors.PermissionUnknown, unknown.Error);
        Assert.Equal(RoleErrors.SystemRoleProtected, system.Error);
        Assert.Equal(RoleErrors.NameAlreadyUsed, duplicate.Error);
    }

    [Fact]
    public async Task A_suspended_member_loses_every_permission_at_once()
    {
        var ownerId = await _fixture.GivenOnboardedTenantAsync();
        await _fixture.Onboard.HandleAsync(new OnboardTenantOwnerCommand(_fixture.TenantA.Value, "second@example.com"));
        var owner = _fixture.Member(ownerId).UserId;
        Assert.True(await _fixture.Checker.HasTenantPermissionAsync(owner, _fixture.TenantA, StaffPermissions.ManageStaff));

        await _fixture.Suspend.HandleAsync(new SuspendStaffCommand(ownerId));

        Assert.False(await _fixture.Checker.HasTenantPermissionAsync(owner, _fixture.TenantA, StaffPermissions.ManageStaff));
        Assert.False(await _fixture.IsActiveMember.HandleAsync(new IsActiveStaffMemberQuery(owner.Value, _fixture.TenantA.Value)));
    }

    [Fact]
    public async Task My_permissions_list_what_the_member_may_do_in_the_selected_restaurant()
    {
        var ownerId = await _fixture.GivenOnboardedTenantAsync();

        var mine = await _fixture.MyPermissions.HandleAsync(new GetMyPermissionsQuery(_fixture.Member(ownerId).UserId.Value));

        Assert.Equal(ownerId, mine.StaffMemberId);
        Assert.Contains(StaffPermissions.ManageStaff, mine.Permissions);
        _fixture.CurrentTenantId = null;
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.MyPermissions.HandleAsync(new GetMyPermissionsQuery(_fixture.Member(ownerId).UserId.Value)));
    }

    [Fact]
    public async Task An_invitation_is_resent_only_while_it_waits_for_an_answer()
    {
        var ownerId = await _fixture.GivenOnboardedTenantAsync();
        var invitedId = await _fixture.Invite.HandleAsync(new InviteStaffCommand("chef@example.com"));
        var chef = _fixture.Account("chef@example.com");

        await _fixture.Resend.HandleAsync(new ResendStaffInvitationCommand(invitedId));
        var resent = Assert.IsType<StaffInvitationResent>(_fixture.Member(invitedId).DomainEvents.Last());
        Assert.Equal(chef, resent.UserId);

        var active = await Assert.ThrowsAsync<DomainException>(() => _fixture.Resend.HandleAsync(new ResendStaffInvitationCommand(ownerId)));
        Assert.Equal(StaffMemberErrors.InvitationAlreadyAccepted, active.Error);
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Resend.HandleAsync(new ResendStaffInvitationCommand(Guid.NewGuid())));
    }

    [Fact]
    public async Task A_custom_role_is_deleted_only_once_nobody_holds_it()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var waiterRole = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));
        var tenantRole = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Comptable"));
        var memberId = await _fixture.Invite.HandleAsync(new InviteStaffCommand("waiter@example.com", tenantRole));
        var here = _fixture.GivenLocation(_fixture.TenantA);
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(memberId, here.Value, waiterRole));
        Assert.Equal(new[] { "Comptable", "Serveur" }, _fixture.Guarded.Select(role => role.Name.Value));

        foreach (var held in new[] { waiterRole, tenantRole })
        {
            var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.DeleteRole.HandleAsync(new DeleteRoleCommand(held)));
            Assert.Equal(RoleErrors.InUse, refused.Error);
        }

        var system = await Assert.ThrowsAsync<DomainException>(() => _fixture.DeleteRole.HandleAsync(
            new DeleteRoleCommand(_fixture.SystemRole(_fixture.TenantA, SystemRoles.Manager).Id.Value)));
        Assert.Equal(RoleErrors.SystemRoleProtected, system.Error);

        await _fixture.Remove.HandleAsync(new RemoveStaffCommand(memberId));
        await _fixture.DeleteRole.HandleAsync(new DeleteRoleCommand(waiterRole));
        await _fixture.DeleteRole.HandleAsync(new DeleteRoleCommand(tenantRole));

        Assert.DoesNotContain(_fixture.ListRoles(_fixture.TenantA), role => role.Id.Value == waiterRole || role.Id.Value == tenantRole);
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.DeleteRole.HandleAsync(new DeleteRoleCommand(waiterRole)));
        Assert.Equal(StaffApplicationErrors.RoleNotFound, gone.Error);
    }
}

namespace Staff.Domain;

public sealed class StaffMemberTests
{
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private StaffMember Invited() => StaffMember.Invite(_tenantId, UserId.New(), isUserUniqueInTenant: true, _clock);

    private StaffMember Active()
    {
        var member = Invited();
        member.Activate(_clock);
        member.ClearDomainEvents();
        return member;
    }

    private StaffMember Suspended()
    {
        var member = Active();
        member.Suspend(_clock);
        member.ClearDomainEvents();
        return member;
    }

    private StaffMember Left()
    {
        var member = Active();
        member.Leave(_clock);
        member.ClearDomainEvents();
        return member;
    }

    private LocationReference Location() => new(_tenantId, LocationId.New(), IsOperational: true);

    private RoleReference Role() => RoleIn(_tenantId);

    // Une référence ne peut provenir que d'un vrai rôle : chaque appel en crée une distincte.
    private RoleReference RoleIn(TenantId tenantId) =>
        Staff.Domain.Role.Create(tenantId, $"Role {Guid.NewGuid():N}", isNameUnique: true, _clock).ToReference();

    private StaffMember Owner(RoleReference ownerRole)
    {
        var member = StaffMember.CreateOwner(_tenantId, UserId.New(), ownerRole, isUserUniqueInTenant: true, _clock);
        member.ClearDomainEvents();
        return member;
    }

    [Fact]
    public void Invite_creates_an_invited_member_and_raises_StaffInvited()
    {
        var userId = UserId.New();

        var member = StaffMember.Invite(_tenantId, userId, isUserUniqueInTenant: true, _clock);

        Assert.Equal(_tenantId, member.TenantId);
        Assert.Equal(userId, member.UserId);
        Assert.Equal(StaffStatus.Invited, member.Status);
        Assert.Equal(_clock.UtcNow, member.InvitedAtUtc);
        Assert.Empty(member.Assignments);
        var raised = DomainAssert.Raised<StaffInvited>(member);
        Assert.Equal(new StaffInvited(_tenantId, member.Id, userId, _clock.UtcNow), raised);
    }

    [Fact]
    public void Invite_rejects_a_user_who_already_has_a_profile_in_the_tenant()
    {
        DomainAssert.Throws(
            StaffMemberErrors.UserAlreadyStaffInTenant,
            () => StaffMember.Invite(_tenantId, UserId.New(), isUserUniqueInTenant: false, _clock));
    }

    [Fact]
    public void Activate_moves_an_invited_member_to_active_and_raises_StaffActivated()
    {
        var member = Invited();
        member.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        member.Activate(_clock);

        Assert.Equal(StaffStatus.Active, member.Status);
        var raised = DomainAssert.Raised<StaffActivated>(member);
        Assert.Equal(new StaffActivated(_tenantId, member.Id, member.UserId, _clock.UtcNow), raised);
    }

    [Fact]
    public void Activate_rejects_an_already_active_member()
    {
        var member = Active();

        DomainAssert.Throws(StaffMemberErrors.NotInvited, () => member.Activate(_clock));
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void Activate_rejects_a_suspended_member()
    {
        var member = Suspended();

        DomainAssert.Throws(StaffMemberErrors.NotInvited, () => member.Activate(_clock));
        Assert.Equal(StaffStatus.Suspended, member.Status);
    }

    [Fact]
    public void AssignToLocation_adds_the_location_and_role_and_raises_StaffAssignedToLocation()
    {
        var member = Active();
        var location = Location();
        var role = Role();

        member.AssignToLocation(location, role, _clock);

        var assignment = Assert.Single(member.Assignments);
        Assert.Equal(location.LocationId, assignment.LocationId);
        Assert.Equal(role.RoleId, assignment.RoleId);
        Assert.True(member.IsAssignedTo(location.LocationId));
        var raised = DomainAssert.Raised<StaffAssignedToLocation>(member);
        Assert.Equal(
            new StaffAssignedToLocation(_tenantId, member.Id, member.UserId, location.LocationId, role.RoleId, _clock.UtcNow),
            raised);
    }

    [Fact]
    public void AssignToLocation_is_allowed_while_still_invited()
    {
        var member = Invited();

        member.AssignToLocation(Location(), Role(), _clock);

        Assert.Single(member.Assignments);
    }

    [Fact]
    public void AssignToLocation_rejects_a_second_assignment_to_the_same_location()
    {
        var member = Active();
        var location = Location();
        member.AssignToLocation(location, Role(), _clock);
        member.ClearDomainEvents();

        DomainAssert.Throws(
            StaffMemberErrors.AlreadyAssignedToLocation,
            () => member.AssignToLocation(location, Role(), _clock));

        Assert.Single(member.Assignments);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void AssignToLocation_rejects_the_exact_same_location_and_role_twice()
    {
        var member = Active();
        var location = Location();
        var role = Role();
        member.AssignToLocation(location, role, _clock);

        DomainAssert.Throws(
            StaffMemberErrors.AlreadyAssignedToLocation,
            () => member.AssignToLocation(location, role, _clock));
    }

    [Fact]
    public void AssignToLocation_accepts_several_distinct_locations()
    {
        var member = Active();
        var role = Role();

        member.AssignToLocation(Location(), role, _clock);
        member.AssignToLocation(Location(), role, _clock);

        Assert.Equal(2, member.Assignments.Count);
    }

    [Fact]
    public void AssignToLocation_rejects_a_location_of_another_tenant()
    {
        var member = Active();
        var foreignLocation = new LocationReference(TenantId.New(), LocationId.New(), IsOperational: true);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => member.AssignToLocation(foreignLocation, Role(), _clock));

        Assert.Empty(member.Assignments);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void AssignToLocation_rejects_a_location_that_is_not_operational()
    {
        var member = Active();
        var closedLocation = new LocationReference(_tenantId, LocationId.New(), IsOperational: false);

        DomainAssert.Throws(
            StaffMemberErrors.LocationNotOperational,
            () => member.AssignToLocation(closedLocation, Role(), _clock));

        Assert.Empty(member.Assignments);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void RemoveFromLocation_does_not_need_the_location_to_be_operational()
    {
        var member = Active();
        var location = Location();
        member.AssignToLocation(location, Role(), _clock);

        // L'établissement a fermé depuis : seul son id est nécessaire pour retirer l'accès.
        member.RemoveFromLocation(location.LocationId, _clock);

        Assert.Empty(member.Assignments);
    }

    [Fact]
    public void LocationReference_rejects_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, LocationId.New(), IsOperational: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(_tenantId, default, IsOperational: true));
    }

    [Fact]
    public void Invite_rejects_an_empty_tenant_or_user()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => StaffMember.Invite(default, UserId.New(), isUserUniqueInTenant: true, _clock));
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => StaffMember.Invite(_tenantId, default, isUserUniqueInTenant: true, _clock));
    }

    [Fact]
    public void CreateOwner_rejects_an_empty_tenant_or_user()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => StaffMember.CreateOwner(default, UserId.New(), Role(), isUserUniqueInTenant: true, _clock));
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => StaffMember.CreateOwner(_tenantId, default, Role(), isUserUniqueInTenant: true, _clock));
    }

    [Fact]
    public void AssignToLocation_rejects_a_role_of_another_tenant()
    {
        var member = Active();
        var foreignRole = RoleIn(TenantId.New());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => member.AssignToLocation(Location(), foreignRole, _clock));

        Assert.Empty(member.Assignments);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void AssignToLocation_rejects_a_suspended_member()
    {
        var member = Suspended();

        DomainAssert.Throws(
            StaffMemberErrors.SuspendedCannotBeAssigned,
            () => member.AssignToLocation(Location(), Role(), _clock));

        Assert.Empty(member.Assignments);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void RemoveFromLocation_removes_the_assignment_and_raises_StaffRemovedFromLocation()
    {
        var member = Active();
        var location = Location();
        var role = Role();
        member.AssignToLocation(location, role, _clock);
        member.ClearDomainEvents();

        member.RemoveFromLocation(location.LocationId, _clock);

        Assert.Empty(member.Assignments);
        Assert.False(member.IsAssignedTo(location.LocationId));
        var raised = DomainAssert.Raised<StaffRemovedFromLocation>(member);
        Assert.Equal(location.LocationId, raised.LocationId);
        Assert.Equal(role.RoleId, raised.RoleId);
    }

    [Fact]
    public void RemoveFromLocation_rejects_a_location_the_member_is_not_assigned_to()
    {
        var member = Active();

        DomainAssert.Throws(
            StaffMemberErrors.NotAssignedToLocation,
            () => member.RemoveFromLocation(LocationId.New(), _clock));
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void RemoveFromLocation_then_AssignToLocation_changes_the_role_at_a_location()
    {
        var member = Active();
        var location = Location();
        var newRole = Role();
        member.AssignToLocation(location, Role(), _clock);

        member.RemoveFromLocation(location.LocationId, _clock);
        member.AssignToLocation(location, newRole, _clock);

        Assert.Equal(newRole.RoleId, Assert.Single(member.Assignments).RoleId);
    }

    [Fact]
    public void RemoveFromLocation_is_allowed_while_suspended()
    {
        var member = Active();
        var location = Location();
        member.AssignToLocation(location, Role(), _clock);
        member.Suspend(_clock);

        member.RemoveFromLocation(location.LocationId, _clock);

        Assert.Empty(member.Assignments);
    }

    [Fact]
    public void Suspend_moves_an_active_member_to_suspended_and_raises_StaffSuspended()
    {
        var member = Active();

        member.Suspend(_clock);

        Assert.Equal(StaffStatus.Suspended, member.Status);
        var raised = DomainAssert.Raised<StaffSuspended>(member);
        Assert.Equal(new StaffSuspended(_tenantId, member.Id, member.UserId, _clock.UtcNow), raised);
    }

    [Fact]
    public void Suspend_keeps_assignments_but_removes_the_effective_role()
    {
        var member = Active();
        var location = Location();
        var role = Role();
        member.AssignToLocation(location, role, _clock);
        Assert.Equal(role.RoleId, member.EffectiveRoleAt(location.LocationId));

        member.Suspend(_clock);

        Assert.Single(member.Assignments);
        Assert.Null(member.EffectiveRoleAt(location.LocationId));
    }

    [Fact]
    public void Suspend_rejects_an_invited_member()
    {
        var member = Invited();
        member.ClearDomainEvents();

        DomainAssert.Throws(StaffMemberErrors.NotActive, () => member.Suspend(_clock));

        Assert.Equal(StaffStatus.Invited, member.Status);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void Suspend_rejects_an_already_suspended_member()
    {
        var member = Suspended();

        DomainAssert.Throws(StaffMemberErrors.NotActive, () => member.Suspend(_clock));
        DomainAssert.NotRaised<StaffSuspended>(member);
    }

    [Fact]
    public void Reinstate_moves_a_suspended_member_back_to_active()
    {
        var member = Suspended();

        member.Reinstate(_clock);

        Assert.Equal(StaffStatus.Active, member.Status);
        DomainAssert.Raised<StaffReinstated>(member);
    }

    [Fact]
    public void Reinstate_restores_the_effective_role_and_allows_assignment_again()
    {
        var member = Active();
        var location = Location();
        var role = Role();
        member.AssignToLocation(location, role, _clock);
        member.Suspend(_clock);

        member.Reinstate(_clock);
        member.AssignToLocation(Location(), Role(), _clock);

        Assert.Equal(role.RoleId, member.EffectiveRoleAt(location.LocationId));
        Assert.Equal(2, member.Assignments.Count);
    }

    [Fact]
    public void Reinstate_rejects_a_member_who_is_not_suspended()
    {
        var member = Active();

        DomainAssert.Throws(StaffMemberErrors.NotSuspended, () => member.Reinstate(_clock));
        DomainAssert.NoEvents(member);
    }

    [Theory]
    [InlineData(StaffStatus.Invited)]
    [InlineData(StaffStatus.Active)]
    [InlineData(StaffStatus.Suspended)]
    public void Leave_is_allowed_from_every_non_terminal_status(StaffStatus from)
    {
        var member = from switch
        {
            StaffStatus.Invited => Invited(),
            StaffStatus.Active => Active(),
            _ => Suspended(),
        };
        member.ClearDomainEvents();

        member.Leave(_clock);

        Assert.Equal(StaffStatus.Left, member.Status);
        var raised = DomainAssert.Raised<StaffLeft>(member);
        Assert.Equal(
            (_tenantId, member.Id, member.UserId, _clock.UtcNow),
            (raised.TenantId, raised.StaffMemberId, raised.UserId, raised.OccurredAtUtc));
        Assert.Empty(raised.RemovedLocationIds);
        Assert.False(raised.HeldTenantWideRole);
    }

    [Fact]
    public void Leave_drops_every_assignment()
    {
        var member = Active();
        var location = Location();
        member.AssignToLocation(location, Role(), _clock);

        member.Leave(_clock);

        Assert.Empty(member.Assignments);
        Assert.Null(member.EffectiveRoleAt(location.LocationId));
    }

    [Fact]
    public void Left_member_rejects_every_action()
    {
        var member = Left();

        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.Activate(_clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.AssignToLocation(Location(), Role(), _clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.RemoveFromLocation(LocationId.New(), _clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.AssignTenantWide(Role(), _clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.RemoveTenantWide(_clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.Suspend(_clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.Reinstate(_clock));
        DomainAssert.Throws(StaffMemberErrors.HasLeft, () => member.Leave(_clock));

        Assert.Equal(StaffStatus.Left, member.Status);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void EffectiveRoleAt_is_null_for_an_invited_member_and_for_an_unassigned_location()
    {
        var invited = Invited();
        var location = Location();
        invited.AssignToLocation(location, Role(), _clock);

        Assert.Null(invited.EffectiveRoleAt(location.LocationId));
        Assert.Null(Active().EffectiveRoleAt(LocationId.New()));
    }

    [Fact]
    public void CreateOwner_builds_an_active_member_holding_the_tenant_wide_owner_role()
    {
        var userId = UserId.New();
        var ownerRole = Role();

        var member = StaffMember.CreateOwner(_tenantId, userId, ownerRole, isUserUniqueInTenant: true, _clock);

        Assert.Equal(_tenantId, member.TenantId);
        Assert.Equal(userId, member.UserId);
        Assert.Equal(StaffStatus.Active, member.Status);
        Assert.Equal(ownerRole.RoleId, member.TenantWideRoleId);
        Assert.Equal(ownerRole.RoleId, member.EffectiveTenantRole);
        Assert.Empty(member.Assignments);
        Assert.Equal(
            [
                new StaffOwnerCreated(_tenantId, member.Id, userId, _clock.UtcNow),
                new StaffActivated(_tenantId, member.Id, userId, _clock.UtcNow),
                new StaffAssignedTenantWide(_tenantId, member.Id, userId, ownerRole.RoleId, _clock.UtcNow),
            ],
            member.DomainEvents);
        DomainAssert.NotRaised<StaffInvited>(member);
    }

    [Fact]
    public void CreateOwner_lets_the_founder_act_before_any_location_exists()
    {
        var ownerRole = Role();
        var owner = Owner(ownerRole);

        // Le premier établissement n'existe pas encore : c'est le rôle à l'échelle du tenant qui autorise sa création.
        Assert.Equal(ownerRole.RoleId, owner.EffectiveTenantRole);
        Assert.Equal(ownerRole.RoleId, owner.EffectiveRoleAt(LocationId.New()));
    }

    [Fact]
    public void CreateOwner_rejects_a_user_who_already_has_a_profile_in_the_tenant()
    {
        DomainAssert.Throws(
            StaffMemberErrors.UserAlreadyStaffInTenant,
            () => StaffMember.CreateOwner(_tenantId, UserId.New(), Role(), isUserUniqueInTenant: false, _clock));
    }

    [Fact]
    public void CreateOwner_rejects_a_role_of_another_tenant()
    {
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => StaffMember.CreateOwner(_tenantId, UserId.New(), RoleIn(TenantId.New()), isUserUniqueInTenant: true, _clock));
    }

    [Fact]
    public void An_owner_follows_the_normal_lifecycle_afterwards()
    {
        var owner = Owner(Role());

        DomainAssert.Throws(StaffMemberErrors.NotInvited, () => owner.Activate(_clock));
        owner.Suspend(_clock);

        Assert.Equal(StaffStatus.Suspended, owner.Status);
        Assert.Null(owner.EffectiveTenantRole);
    }

    [Fact]
    public void AssignTenantWide_sets_the_role_and_raises_StaffAssignedTenantWide()
    {
        var member = Active();
        var role = Role();

        member.AssignTenantWide(role, _clock);

        Assert.Equal(role.RoleId, member.TenantWideRoleId);
        Assert.Equal(role.RoleId, member.EffectiveTenantRole);
        Assert.Empty(member.Assignments);
        var raised = DomainAssert.Raised<StaffAssignedTenantWide>(member);
        Assert.Equal(new StaffAssignedTenantWide(_tenantId, member.Id, member.UserId, role.RoleId, _clock.UtcNow), raised);
    }

    [Fact]
    public void AssignTenantWide_is_allowed_while_still_invited_but_not_effective()
    {
        var member = Invited();
        var role = Role();

        member.AssignTenantWide(role, _clock);

        Assert.Equal(role.RoleId, member.TenantWideRoleId);
        Assert.Null(member.EffectiveTenantRole);
        Assert.Null(member.EffectiveRoleAt(LocationId.New()));
    }

    [Fact]
    public void AssignTenantWide_rejects_a_second_tenant_wide_role()
    {
        var member = Active();
        var role = Role();
        member.AssignTenantWide(role, _clock);
        member.ClearDomainEvents();

        DomainAssert.Throws(StaffMemberErrors.AlreadyAssignedTenantWide, () => member.AssignTenantWide(Role(), _clock));
        DomainAssert.Throws(StaffMemberErrors.AlreadyAssignedTenantWide, () => member.AssignTenantWide(role, _clock));

        Assert.Equal(role.RoleId, member.TenantWideRoleId);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void AssignTenantWide_rejects_a_role_of_another_tenant()
    {
        var member = Active();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => member.AssignTenantWide(RoleIn(TenantId.New()), _clock));

        Assert.Null(member.TenantWideRoleId);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void AssignTenantWide_rejects_a_suspended_member()
    {
        var member = Suspended();

        DomainAssert.Throws(StaffMemberErrors.SuspendedCannotBeAssigned, () => member.AssignTenantWide(Role(), _clock));

        Assert.Null(member.TenantWideRoleId);
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void RemoveTenantWide_clears_the_role_and_raises_StaffTenantWideAssignmentRemoved()
    {
        var member = Active();
        var role = Role();
        member.AssignTenantWide(role, _clock);
        member.ClearDomainEvents();

        member.RemoveTenantWide(_clock);

        Assert.Null(member.TenantWideRoleId);
        Assert.Null(member.EffectiveTenantRole);
        var raised = DomainAssert.Raised<StaffTenantWideAssignmentRemoved>(member);
        Assert.Equal(
            new StaffTenantWideAssignmentRemoved(_tenantId, member.Id, member.UserId, role.RoleId, _clock.UtcNow),
            raised);
    }

    [Fact]
    public void RemoveTenantWide_rejects_a_member_without_tenant_wide_role()
    {
        var member = Active();

        DomainAssert.Throws(StaffMemberErrors.NotAssignedTenantWide, () => member.RemoveTenantWide(_clock));
        DomainAssert.NoEvents(member);
    }

    [Fact]
    public void RemoveTenantWide_is_allowed_while_suspended_and_then_a_new_role_can_be_assigned_once_reinstated()
    {
        var member = Active();
        var newRole = Role();
        member.AssignTenantWide(Role(), _clock);
        member.Suspend(_clock);

        member.RemoveTenantWide(_clock);
        member.Reinstate(_clock);
        member.AssignTenantWide(newRole, _clock);

        Assert.Equal(newRole.RoleId, member.EffectiveTenantRole);
    }

    [Fact]
    public void EffectiveRoleAt_prefers_the_location_role_and_falls_back_to_the_tenant_wide_role()
    {
        var member = Active();
        var tenantRole = Role();
        var locationRole = Role();
        var assigned = Location();
        member.AssignTenantWide(tenantRole, _clock);
        member.AssignToLocation(assigned, locationRole, _clock);

        Assert.Equal(locationRole.RoleId, member.EffectiveRoleAt(assigned.LocationId));
        Assert.Equal(tenantRole.RoleId, member.EffectiveRoleAt(LocationId.New()));
        Assert.Equal(tenantRole.RoleId, member.EffectiveTenantRole);

        member.RemoveFromLocation(assigned.LocationId, _clock);
        Assert.Equal(tenantRole.RoleId, member.EffectiveRoleAt(assigned.LocationId));

        member.RemoveTenantWide(_clock);
        Assert.Null(member.EffectiveRoleAt(assigned.LocationId));
    }

    [Fact]
    public void EffectiveTenantRole_ignores_location_assignments()
    {
        var member = Active();
        member.AssignToLocation(Location(), Role(), _clock);

        Assert.Null(member.EffectiveTenantRole);
    }

    [Fact]
    public void Suspend_neutralises_the_tenant_wide_role_and_Reinstate_restores_it()
    {
        var member = Active();
        var role = Role();
        member.AssignTenantWide(role, _clock);

        member.Suspend(_clock);
        Assert.Equal(role.RoleId, member.TenantWideRoleId);
        Assert.Null(member.EffectiveTenantRole);
        Assert.Null(member.EffectiveRoleAt(LocationId.New()));

        member.Reinstate(_clock);
        Assert.Equal(role.RoleId, member.EffectiveTenantRole);
    }

    [Fact]
    public void Leave_clears_the_tenant_wide_role_and_StaffLeft_lists_what_was_removed()
    {
        var member = Active();
        var first = Location();
        var second = Location();
        member.AssignToLocation(first, Role(), _clock);
        member.AssignToLocation(second, Role(), _clock);
        member.AssignTenantWide(Role(), _clock);
        member.ClearDomainEvents();

        member.Leave(_clock);

        Assert.Empty(member.Assignments);
        Assert.Null(member.TenantWideRoleId);
        Assert.Null(member.EffectiveTenantRole);
        Assert.Null(member.EffectiveRoleAt(first.LocationId));
        var raised = DomainAssert.Raised<StaffLeft>(member);
        Assert.Equal([first.LocationId, second.LocationId], raised.RemovedLocationIds);
        Assert.True(raised.HeldTenantWideRole);
        DomainAssert.NotRaised<StaffRemovedFromLocation>(member);
        DomainAssert.NotRaised<StaffTenantWideAssignmentRemoved>(member);
    }

    [Fact]
    public void StaffAssignment_is_compared_by_location_and_role()
    {
        var location = Location();
        var role = Role();
        var first = Active();
        var second = Active();
        first.AssignToLocation(location, role, _clock);
        second.AssignToLocation(location, role, _clock);

        Assert.Equal(first.Assignments.Single(), second.Assignments.Single());
    }

    [Fact]
    public void StaffMemberId_rejects_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => StaffMemberId.From(Guid.Empty));
        Assert.NotEqual(StaffMemberId.New(), StaffMemberId.New());
    }
}

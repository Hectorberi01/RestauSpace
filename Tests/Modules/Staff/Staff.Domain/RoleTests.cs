namespace Staff.Domain;

public sealed class RoleTests
{
    private static readonly PermissionCode OrdersCancel = PermissionCode.Create("orders.cancel");
    private static readonly PermissionCode OrdersRefund = PermissionCode.Create("orders.refund");
    private static readonly PermissionCode Unknown = PermissionCode.Create("spaceship.launch");
    private static readonly PermissionCatalog Catalog = PermissionCatalog.Create([OrdersCancel, OrdersRefund]);

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private Role CustomRole(string name = "Waiter")
    {
        var role = Role.Create(_tenantId, name, isNameUnique: true, _clock);
        role.ClearDomainEvents();
        return role;
    }

    private Role SystemRole()
    {
        var role = Role.CreateSystem(_tenantId, "Owner", [OrdersCancel], Catalog, isNameUnique: true, _clock);
        role.ClearDomainEvents();
        return role;
    }

    [Fact]
    public void Create_builds_an_empty_custom_role_and_raises_RoleCreated()
    {
        var role = Role.Create(_tenantId, "  Waiter  ", isNameUnique: true, _clock);

        Assert.Equal(_tenantId, role.TenantId);
        Assert.Equal("Waiter", role.Name.Value);
        Assert.False(role.IsSystem);
        Assert.Empty(role.Permissions);
        var raised = DomainAssert.Raised<RoleCreated>(role);
        Assert.Equal(role.Id, raised.RoleId);
        Assert.Equal(_tenantId, raised.TenantId);
        Assert.Equal("Waiter", raised.Name);
        Assert.False(raised.IsSystem);
        Assert.Empty(raised.Permissions);
        Assert.Equal(_clock.UtcNow, raised.OccurredAtUtc);
    }

    [Fact]
    public void Create_rejects_a_name_already_used_in_the_tenant()
    {
        DomainAssert.Throws(RoleErrors.NameAlreadyUsed, () => Role.Create(_tenantId, "Waiter", isNameUnique: false, _clock));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string? name)
    {
        DomainAssert.Throws(RoleErrors.NameRequired, () => Role.Create(_tenantId, name, isNameUnique: true, _clock));
    }

    [Fact]
    public void Create_enforces_the_name_length_limit()
    {
        var atLimit = Role.Create(_tenantId, new string('a', RoleName.MaxLength), isNameUnique: true, _clock);

        Assert.Equal(RoleName.MaxLength, atLimit.Name.Value.Length);
        DomainAssert.Throws(
            RoleErrors.NameTooLong,
            () => Role.Create(_tenantId, new string('a', RoleName.MaxLength + 1), isNameUnique: true, _clock));
    }

    [Fact]
    public void CreateSystem_builds_a_protected_role_with_its_permissions()
    {
        var role = Role.CreateSystem(_tenantId, "Owner", [OrdersCancel, OrdersRefund, OrdersCancel], Catalog, isNameUnique: true, _clock);

        Assert.True(role.IsSystem);
        Assert.Equal(2, role.Permissions.Count);
        Assert.True(role.HasPermission(OrdersCancel));
        Assert.True(role.HasPermission(OrdersRefund));
        var raised = DomainAssert.Raised<RoleCreated>(role);
        Assert.True(raised.IsSystem);
        Assert.Equal([OrdersCancel, OrdersRefund], raised.Permissions);
        DomainAssert.NotRaised<RolePermissionGranted>(role);
    }

    [Fact]
    public void CreateSystem_rejects_a_permission_outside_the_catalog()
    {
        DomainAssert.Throws(
            RoleErrors.PermissionUnknown,
            () => Role.CreateSystem(_tenantId, "Owner", [Unknown], Catalog, isNameUnique: true, _clock));
    }

    [Fact]
    public void CreateSystem_rejects_a_name_already_used_in_the_tenant()
    {
        DomainAssert.Throws(
            RoleErrors.NameAlreadyUsed,
            () => Role.CreateSystem(_tenantId, "Owner", [OrdersCancel], Catalog, isNameUnique: false, _clock));
    }

    [Fact]
    public void Rename_changes_the_name_and_raises_RoleRenamed()
    {
        var role = CustomRole("Waiter");

        role.Rename(" Head waiter ", isNameUnique: true, _clock);

        Assert.Equal("Head waiter", role.Name.Value);
        var raised = DomainAssert.Raised<RoleRenamed>(role);
        Assert.Equal(new RoleRenamed(_tenantId, role.Id, "Waiter", "Head waiter", _clock.UtcNow), raised);
    }

    [Fact]
    public void Rename_rejects_a_name_already_used_in_the_tenant()
    {
        var role = CustomRole("Waiter");

        DomainAssert.Throws(RoleErrors.NameAlreadyUsed, () => role.Rename("Manager", isNameUnique: false, _clock));

        Assert.Equal("Waiter", role.Name.Value);
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Rename_rejects_an_empty_name()
    {
        var role = CustomRole();

        DomainAssert.Throws(RoleErrors.NameRequired, () => role.Rename(" ", isNameUnique: true, _clock));
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        var role = CustomRole("Waiter");

        role.Rename("Waiter", isNameUnique: false, _clock);

        Assert.Equal("Waiter", role.Name.Value);
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Rename_to_a_different_casing_of_its_own_name_does_not_need_uniqueness()
    {
        var role = CustomRole("Waiter");

        role.Rename("WAITER", isNameUnique: false, _clock);

        Assert.Equal("WAITER", role.Name.Value);
        DomainAssert.Raised<RoleRenamed>(role);
    }

    [Fact]
    public void Grant_adds_a_known_permission_and_raises_RolePermissionGranted()
    {
        var role = CustomRole();

        role.Grant(OrdersCancel, Catalog, _clock);

        Assert.True(role.HasPermission(OrdersCancel));
        Assert.Equal(OrdersCancel, Assert.Single(role.Permissions));
        var raised = DomainAssert.Raised<RolePermissionGranted>(role);
        Assert.Equal(new RolePermissionGranted(_tenantId, role.Id, OrdersCancel, _clock.UtcNow), raised);
    }

    [Fact]
    public void Grant_rejects_a_permission_outside_the_catalog()
    {
        var role = CustomRole();

        DomainAssert.Throws(RoleErrors.PermissionUnknown, () => role.Grant(Unknown, Catalog, _clock));

        Assert.Empty(role.Permissions);
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Grant_rejects_a_permission_already_granted()
    {
        var role = CustomRole();
        role.Grant(OrdersCancel, Catalog, _clock);
        role.ClearDomainEvents();

        DomainAssert.Throws(RoleErrors.PermissionAlreadyGranted, () => role.Grant(OrdersCancel, Catalog, _clock));

        Assert.Single(role.Permissions);
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Revoke_removes_a_granted_permission_and_raises_RolePermissionRevoked()
    {
        var role = CustomRole();
        role.Grant(OrdersCancel, Catalog, _clock);
        role.Grant(OrdersRefund, Catalog, _clock);
        role.ClearDomainEvents();

        role.Revoke(OrdersCancel, _clock);

        Assert.False(role.HasPermission(OrdersCancel));
        Assert.True(role.HasPermission(OrdersRefund));
        var raised = DomainAssert.Raised<RolePermissionRevoked>(role);
        Assert.Equal(new RolePermissionRevoked(_tenantId, role.Id, OrdersCancel, _clock.UtcNow), raised);
    }

    [Fact]
    public void Revoke_rejects_a_permission_that_was_never_granted()
    {
        var role = CustomRole();

        DomainAssert.Throws(RoleErrors.PermissionNotGranted, () => role.Revoke(OrdersCancel, _clock));
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Revoke_then_Grant_restores_the_permission()
    {
        var role = CustomRole();
        role.Grant(OrdersCancel, Catalog, _clock);
        role.Revoke(OrdersCancel, _clock);

        role.Grant(OrdersCancel, Catalog, _clock);

        Assert.True(role.HasPermission(OrdersCancel));
    }

    [Fact]
    public void Revoke_works_for_a_permission_that_left_the_catalog()
    {
        var role = CustomRole();
        role.Grant(OrdersCancel, Catalog, _clock);

        // Aucun catalogue requis : retirer une permission obsolète doit toujours être possible.
        role.Revoke(OrdersCancel, _clock);

        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void System_role_rejects_rename_grant_and_revoke()
    {
        var role = SystemRole();

        DomainAssert.Throws(RoleErrors.SystemRoleProtected, () => role.Rename("Boss", isNameUnique: true, _clock));
        DomainAssert.Throws(RoleErrors.SystemRoleProtected, () => role.Grant(OrdersRefund, Catalog, _clock));
        DomainAssert.Throws(RoleErrors.SystemRoleProtected, () => role.Revoke(OrdersCancel, _clock));

        Assert.Equal("Owner", role.Name.Value);
        Assert.Equal(OrdersCancel, Assert.Single(role.Permissions));
        DomainAssert.NoEvents(role);
    }

    [Fact]
    public void Permissions_cannot_be_mutated_from_outside()
    {
        var role = CustomRole();
        role.Grant(OrdersCancel, Catalog, _clock);

        Assert.False(role.Permissions is HashSet<PermissionCode>);
        Assert.Throws<NotSupportedException>(() => ((ICollection<PermissionCode>)role.Permissions).Add(OrdersRefund));
    }

    [Fact]
    public void ToReference_carries_the_role_tenant_and_id()
    {
        var role = CustomRole();

        var reference = role.ToReference();

        Assert.Equal(_tenantId, reference.TenantId);
        Assert.Equal(role.Id, reference.RoleId);
        Assert.Equal(reference, role.ToReference());
    }

    [Fact]
    public void A_role_reference_can_only_be_built_by_its_role()
    {
        // Pas de constructeur public : un appelant ne peut pas falsifier le tenant auquel appartient un rôle.
        Assert.Empty(typeof(RoleReference).GetConstructors());
    }

    [Fact]
    public void A_role_reference_lets_a_staff_member_of_the_same_tenant_be_assigned()
    {
        var role = CustomRole();
        var member = StaffMember.Invite(_tenantId, UserId.New(), isUserUniqueInTenant: true, _clock);

        member.AssignToLocation(new LocationReference(_tenantId, LocationId.New(), IsOperational: true), role.ToReference(), _clock);

        Assert.Equal(role.Id, Assert.Single(member.Assignments).RoleId);
    }

    [Fact]
    public void RoleName_ignores_case_when_compared()
    {
        Assert.Equal(RoleName.Create("Manager"), RoleName.Create(" manager "));
        Assert.NotEqual(RoleName.Create("Manager"), RoleName.Create("Waiter"));
    }

    [Fact]
    public void RoleName_collapses_internal_whitespace_so_spacing_variants_are_one_name()
    {
        var name = RoleName.Create("  Chef  de\t rang\u00A0");

        Assert.Equal("Chef de rang", name.Value);
        Assert.Equal(RoleName.Create("Chef de rang"), name);
    }

    [Fact]
    public void RoleName_applies_compatibility_normalisation_before_comparing()
    {
        // Des lettres pleine chasse et un accent décomposé donnent le même nom une fois normalisés.
        Assert.Equal("Chef", RoleName.Create("\uFF23\uFF48\uFF45\uFF46").Value);
        Assert.Equal(RoleName.Create("Caf\u00E9"), RoleName.Create("Cafe\u0301"));
        Assert.Equal(4, RoleName.Create("Cafe\u0301").Value.Length);
    }

    [Fact]
    public void RoleName_removes_control_and_format_characters()
    {
        Assert.Equal("Manager", RoleName.Create("Man\u200Bag\u0000er\uFEFF").Value);
        Assert.Equal(RoleName.Create("Manager"), RoleName.Create("\u202EManager"));
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200B\u200D\uFEFF")]
    [InlineData(" \u00A0\u2003 ")]
    [InlineData("\u0000\u0007")]
    public void RoleName_rejects_a_name_made_only_of_invisible_characters(string name)
    {
        DomainAssert.Throws(RoleErrors.NameRequired, () => RoleName.Create(name));
    }

    [Fact]
    public void RoleName_drops_unpaired_surrogates_instead_of_failing_on_ill_formed_text()
    {
        var loneSurrogate = ((char)0xD800).ToString();

        DomainAssert.Throws(RoleErrors.NameRequired, () => RoleName.Create(loneSurrogate));
        Assert.Equal("Chef", RoleName.Create("Ch" + loneSurrogate + "ef").Value);
    }

    [Fact]
    public void RoleName_measures_the_length_after_normalisation()
    {
        // La longueur brute dépasse la limite, la graphie canonique est exactement à la limite.
        var raw = new string('a', RoleName.MaxLength - 2) + "   \u200B  b";

        Assert.Equal(RoleName.MaxLength, RoleName.Create(raw).Value.Length);
        DomainAssert.Throws(RoleErrors.NameTooLong, () => RoleName.Create("a" + raw));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Role.Create(default, "Waiter", isNameUnique: true, _clock));
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => Role.CreateSystem(default, "Owner", [OrdersCancel], Catalog, isNameUnique: true, _clock));
    }

    [Fact]
    public void RoleId_rejects_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => RoleId.From(Guid.Empty));
    }

    [Fact]
    public void A_system_role_is_completed_with_new_module_permissions_and_keeps_the_others()
    {
        var role = Role.CreateSystem(_tenantId, "Owner", [OrdersCancel], Catalog, isNameUnique: true, _clock);
        var custom = Role.Create(_tenantId, "Caisse", isNameUnique: true, _clock);
        role.ClearDomainEvents();

        var added = role.CompleteSystemPermissions([OrdersCancel, OrdersRefund, Unknown], Catalog, _clock);
        var again = role.CompleteSystemPermissions([OrdersCancel, OrdersRefund], Catalog, _clock);

        Assert.Equal(1, added);
        Assert.Equal(0, again);
        Assert.True(role.HasPermission(OrdersRefund));
        Assert.True(role.HasPermission(OrdersCancel));
        Assert.Single(role.DomainEvents);
        DomainAssert.Throws(RoleErrors.NotSystemRole, () => custom.CompleteSystemPermissions([OrdersCancel], Catalog, _clock));
    }
}

using System.Collections.ObjectModel;

namespace Staff.Domain;

/// <summary>Ensemble nommé de permissions d'un tenant. Un rôle système est créé par la plateforme et est en
/// lecture seule.</summary>
public sealed class Role : AggregateRoot<RoleId>, ITenantOwned
{
    private readonly HashSet<PermissionCode> _permissions = [];

    private Role(RoleId id, TenantId tenantId, RoleName name, bool isSystem)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        IsSystem = isSystem;
    }

    // Réservé à l'ORM.
    private Role()
    {
        Name = null!;
    }

    public TenantId TenantId { get; private set; }

    public RoleName Name { get; private set; }

    public bool IsSystem { get; private set; }

    public IReadOnlySet<PermissionCode> Permissions => new ReadOnlySet<PermissionCode>(_permissions);

    /// <summary>Crée un rôle personnalisé vide ; les permissions sont ensuite accordées une à une.</summary>
    public static Role Create(TenantId tenantId, string? name, bool isNameUnique, IClock clock) =>
        CreateCore(tenantId, name, isSystem: false, [], catalog: null, isNameUnique, clock);

    /// <summary>
    /// Crée un rôle système protégé. Ses permissions sont fixées ici car Grant/Revoke sont refusés ensuite.
    /// </summary>
    public static Role CreateSystem(
        TenantId tenantId,
        string? name,
        IEnumerable<PermissionCode> permissions,
        PermissionCatalog catalog,
        bool isNameUnique,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(catalog);
        return CreateCore(tenantId, name, isSystem: true, permissions, catalog, isNameUnique, clock);
    }

    public void Rename(string? name, bool isNameUnique, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotSystem();

        var newName = RoleName.Create(name);
        if (newName.Value == Name.Value)
        {
            return;
        }

        // Un simple changement de casse garde le même nom (insensible à la casse) : pas de collision possible
        // avec un autre rôle.
        DomainException.ThrowIf(newName != Name && !isNameUnique, RoleErrors.NameAlreadyUsed);

        var previous = Name;
        Name = newName;
        Raise(new RoleRenamed(TenantId, Id, previous.Value, newName.Value, clock.UtcNow));
    }

    public void Grant(PermissionCode permission, PermissionCatalog catalog, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotSystem();
        DomainException.ThrowIf(!catalog.Contains(permission), RoleErrors.PermissionUnknown);
        DomainException.ThrowIf(_permissions.Contains(permission), RoleErrors.PermissionAlreadyGranted);

        _permissions.Add(PermissionCode.Create(permission.Value));
        Raise(new RolePermissionGranted(TenantId, Id, permission, clock.UtcNow));
    }

    public void Revoke(PermissionCode permission, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotSystem();
        DomainException.ThrowIf(!_permissions.Contains(permission), RoleErrors.PermissionNotGranted);

        _permissions.Remove(permission);
        Raise(new RolePermissionRevoked(TenantId, Id, permission, clock.UtcNow));
    }

    /// <summary>Supprime un rôle personnalisé : refusé tant qu'un membre (invité, actif ou suspendu) le détient.</summary>
    public void Delete(bool isHeld, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotSystem();
        DomainException.ThrowIf(isHeld, RoleErrors.InUse);
        Raise(new RoleDeleted(TenantId, Id, Name.Value, clock.UtcNow));
    }

    public bool HasPermission(PermissionCode permission) => _permissions.Contains(permission);

    /// <summary>
    /// Rôle système seulement : accorde les permissions attendues qui lui manquent (un module ajouté après la création du
    /// rôle). Ne retire jamais rien. Renvoie le nombre de permissions ajoutées.
    /// </summary>
    public int CompleteSystemPermissions(IEnumerable<PermissionCode> expected, PermissionCatalog catalog, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsSystem, RoleErrors.NotSystemRole);
        var added = 0;
        foreach (var permission in expected.Where(permission => catalog.Contains(permission) && !_permissions.Contains(permission)).ToList())
        {
            _permissions.Add(PermissionCode.Create(permission.Value));
            Raise(new RolePermissionGranted(TenantId, Id, permission, clock.UtcNow));
            added++;
        }

        return added;
    }

    public RoleReference ToReference() => new(TenantId, Id);

    private static Role CreateCore(
        TenantId tenantId,
        string? name,
        bool isSystem,
        IEnumerable<PermissionCode> permissions,
        PermissionCatalog? catalog,
        bool isNameUnique,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var roleName = RoleName.Create(name);
        DomainException.ThrowIf(!isNameUnique, RoleErrors.NameAlreadyUsed);

        var role = new Role(RoleId.New(), tenantId, roleName, isSystem);
        foreach (var permission in permissions)
        {
            ArgumentNullException.ThrowIfNull(permission);
            DomainException.ThrowIf(!catalog!.Contains(permission), RoleErrors.PermissionUnknown);
            role._permissions.Add(PermissionCode.Create(permission.Value));
        }

        role.Raise(new RoleCreated(
            tenantId,
            role.Id,
            roleName.Value,
            isSystem,
            role._permissions.OrderBy(p => p.Value, StringComparer.Ordinal).ToList().AsReadOnly(),
            clock.UtcNow));
        return role;
    }

    private void EnsureNotSystem() => DomainException.ThrowIf(IsSystem, RoleErrors.SystemRoleProtected);
}

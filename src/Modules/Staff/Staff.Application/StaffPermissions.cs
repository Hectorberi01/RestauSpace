using Staff.Domain;

namespace Staff.Application;

/// <summary>Codes de permission déclarés par le module Staff lui-même. Les autres modules déclarent les leurs pareil.</summary>
public static class StaffPermissions
{
    /// <summary>Inviter, affecter, suspendre le personnel et changer ses rôles.</summary>
    public const string ManageStaff = "staff.manage";

    /// <summary>Créer des rôles personnalisés et changer leurs permissions.</summary>
    public const string ManageRoles = "roles.manage";

    /// <summary>Établir et publier le planning, valider absences et remplacements, voir l'écart avec les pointages.</summary>
    public const string Schedule = "staff.schedule";

    public static IReadOnlyList<string> All { get; } = [ManageStaff, ManageRoles, Schedule];
}

/// <summary>
/// Rôles système créés pour chaque tenant à l'onboarding. Le tenant ne peut pas les modifier (règle du domaine) ; quand
/// le catalogue s'agrandit, les rôles système existants sont mis à jour par une migration de la plateforme.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";

    /// <summary>Owner : toutes les permissions. Manager : toutes sauf la gestion des personnes et des droits.</summary>
    public static IReadOnlyList<PermissionCode> PermissionsOf(string systemRole, PermissionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return systemRole switch
        {
            Owner => catalog.Codes.ToList(),
            Manager => catalog.Codes
                .Where(code => code.Value is not (StaffPermissions.ManageStaff or StaffPermissions.ManageRoles))
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(systemRole), systemRole, "Unknown system role."),
        };
    }

    public static bool IsOwnerRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.IsSystem && role.Name.Value == Owner;
    }
}

/// <summary>Catalogue de permissions de toute la plateforme, assemblé par l'hôte depuis les codes de chaque module.</summary>
public sealed class KnownPermissions(PermissionCatalog catalog)
{
    public PermissionCatalog Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public static KnownPermissions Of(IEnumerable<string> codes) =>
        new(PermissionCatalog.Of(codes.Concat(StaffPermissions.All).Distinct(StringComparer.Ordinal).ToArray()));
}

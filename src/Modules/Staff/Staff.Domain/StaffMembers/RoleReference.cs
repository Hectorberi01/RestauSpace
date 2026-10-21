namespace Staff.Domain;

/// <summary>
/// Référence à un <see cref="Role"/> portant son tenant propriétaire. Seul <see cref="Role.ToReference"/> peut la
/// construire : le tenant qu'elle porte est donc toujours le vrai propriétaire du rôle.
/// </summary>
public sealed record RoleReference
{
    internal RoleReference(TenantId tenantId, RoleId roleId)
    {
        TenantId = tenantId;
        RoleId = roleId;
    }

    public TenantId TenantId { get; }

    public RoleId RoleId { get; }
}

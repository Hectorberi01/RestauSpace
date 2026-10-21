using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application;

/// <summary>
/// Répond à <see cref="IPermissionChecker"/> à partir des rôles du membre du personnel, sans cache : une permission
/// révoquée, une suspension ou un départ prend effet à la requête suivante.
/// </summary>
public sealed class StaffPermissionChecker(IStaffMemberRepository members, IRoleRepository roles) : IPermissionChecker
{
    public async Task<bool> HasTenantPermissionAsync(
        UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default)
    {
        var member = await members.FindByUserAsync(tenantId, userId, cancellationToken);
        return member?.EffectiveTenantRole is { } roleId && await RoleGrantsAsync(tenantId, roleId, permission, cancellationToken);
    }

    public async Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default)
    {
        var member = await members.FindByUserAsync(tenantId, userId, cancellationToken);
        return member?.EffectiveRoleAt(locationId) is { } roleId && await RoleGrantsAsync(tenantId, roleId, permission, cancellationToken);
    }

    private async Task<bool> RoleGrantsAsync(TenantId tenantId, RoleId roleId, string permission, CancellationToken cancellationToken)
    {
        var role = await roles.FindAsync(tenantId, roleId, cancellationToken);
        return role is not null && role.HasPermission(PermissionCode.Create(permission));
    }
}

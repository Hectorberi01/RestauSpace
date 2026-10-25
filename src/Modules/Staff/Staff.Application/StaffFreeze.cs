using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application;

/// <summary>
/// Gel du personnel, calculé à l'usage : les membres non partis sont rangés propriétaire en tête, puis par date
/// d'invitation ; au-delà de la limite du plan, ils sont gelés (sélection du restaurant et caisse refusées).
/// </summary>
public sealed class StaffFreeze(IStaffMemberRepository members, IRoleRepository roles, IStaffPlanLimits limits)
{
    public async Task<IReadOnlySet<StaffMemberId>> FrozenAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (await limits.MaxStaffMembersAsync(tenantId, cancellationToken) is not { } limit)
        {
            return new HashSet<StaffMemberId>();
        }

        var owner = await roles.FindSystemRoleAsync(tenantId, SystemRoles.Owner, cancellationToken);
        return (await members.ListCurrentAsync(tenantId, cancellationToken))
            .OrderByDescending(member => owner is not null && member.TenantWideRoleId == owner.Id)
            .ThenBy(member => member.InvitedAtUtc)
            .ThenBy(member => member.Id.Value)
            .Skip((int)Math.Clamp(limit, 0, int.MaxValue))
            .Select(member => member.Id)
            .ToHashSet();
    }

    public async Task<bool> IsFrozenAsync(TenantId tenantId, StaffMemberId memberId, CancellationToken cancellationToken = default) =>
        (await FrozenAsync(tenantId, cancellationToken)).Contains(memberId);
}

using Staff.Domain;

namespace Staff.Application.Queries;

public sealed record StaffAssignmentDto(Guid LocationId, Guid RoleId);

/// <param name="Email">Email du compte (Identity) ; null si le compte n'existe plus.</param>
public sealed record StaffMemberDto(
    Guid Id,
    Guid UserId,
    StaffStatus Status,
    Guid? TenantWideRoleId,
    IReadOnlyList<StaffAssignmentDto> Assignments,
    DateTimeOffset InvitedAtUtc,
    string? Email = null,
    bool HasPin = false,
    bool IsFrozen = false)
{
    /// <param name="isFrozen">Au-delà de la limite du plan (<see cref="StaffFreeze"/>).</param>
    public static StaffMemberDto From(StaffMember member, string? email = null, bool isFrozen = false)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new StaffMemberDto(
            member.Id.Value,
            member.UserId.Value,
            member.Status,
            member.TenantWideRoleId?.Value,
            member.Assignments.Select(assignment => new StaffAssignmentDto(assignment.LocationId.Value, assignment.RoleId.Value)).ToList(),
            member.InvitedAtUtc,
            email,
            member.HasPin,
            isFrozen);
    }
}


public sealed record RoleDto(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions)
{
    public static RoleDto From(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new RoleDto(
            role.Id.Value,
            role.Name.Value,
            role.IsSystem,
            role.Permissions.Select(permission => permission.Value).Order(StringComparer.Ordinal).ToList());
    }
}

/// <summary>Un restaurant auquel appartient l'utilisateur (les profils Left ne sont pas listés).</summary>
public sealed record MembershipDto(Guid TenantId, string TenantName, Guid StaffMemberId, StaffStatus Status);

/// <summary>Ce que le membre connecté peut faire au niveau du tenant dans le restaurant sélectionné.</summary>
public sealed record MyPermissionsDto(
    Guid StaffMemberId,
    StaffStatus Status,
    Guid? TenantWideRoleId,
    IReadOnlyList<string> Permissions,
    bool HasPin = false,
    IReadOnlyList<MyLocationPermissionsDto>? Locations = null);

/// <summary>Permissions dans un établissement où le membre a un rôle propre (il l'emporte sur le rôle de tenant).</summary>
public sealed record MyLocationPermissionsDto(Guid LocationId, Guid RoleId, IReadOnlyList<string> Permissions);

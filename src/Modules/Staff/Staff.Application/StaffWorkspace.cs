using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application;

/// <summary>
/// Étapes partagées par les cas d'usage : le tenant vient du contexte de requête (jamais de la requête), les écritures
/// exigent un tenant qui les accepte, membres et rôles sont chargés dans ce seul tenant (404 sinon), et la règle
/// « au moins un propriétaire actif », qui porte sur plusieurs membres, est vérifiée ici.
/// </summary>
public sealed class StaffWorkspace(
    ITenantContext tenantContext,
    IStaffTenants tenants,
    IStaffMemberRepository members,
    IRoleRepository roles,
    IStaffUnitOfWork unitOfWork,
    IStaffActor actor)
{
    public IStaffMemberRepository Members => members;

    public IRoleRepository Roles => roles;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Le tenant courant, refusé s'il est suspendu, fermé ou inconnu.</summary>
    public async Task<TenantId> RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenant;
        await EnsureWritableAsync(tenantId, cancellationToken);
        return tenantId;
    }

    public async Task EnsureWritableAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var access = await tenants.GetAccessAsync(tenantId, cancellationToken);
        if (access == TenantAccess.Unknown)
        {
            throw new NotFoundException(StaffApplicationErrors.TenantNotFound);
        }

        if (access != TenantAccess.Writable)
        {
            throw new DomainException(StaffApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<StaffMember> LoadMemberAsync(TenantId tenantId, Guid memberId, CancellationToken cancellationToken)
    {
        var member = memberId == Guid.Empty
            ? null
            : await members.FindAsync(tenantId, StaffMemberId.From(memberId), cancellationToken);
        return member ?? throw new NotFoundException(StaffApplicationErrors.MemberNotFound);
    }

    /// <summary>Le profil de l'utilisateur connecté dans ce restaurant.</summary>
    public async Task<StaffMember> LoadSelfAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var member = userId == Guid.Empty ? null : await members.FindByUserAsync(tenantId, UserId.From(userId), cancellationToken);
        return member ?? throw new NotFoundException(StaffApplicationErrors.MemberNotFound);
    }

    public async Task<Role> LoadRoleAsync(TenantId tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        var role = roleId == Guid.Empty
            ? null
            : await roles.FindAsync(tenantId, RoleId.From(roleId), cancellationToken);
        return role ?? throw new NotFoundException(StaffApplicationErrors.RoleNotFound);
    }

    /// <summary>
    /// Avant un changement qui retirerait le rôle Owner à un membre actif (suspension, départ, retrait du rôle à
    /// l'échelle du tenant) : un autre propriétaire actif doit rester. Deux retraits concurrents ne peuvent pas
    /// passer tous les deux : chacun réécrit la ligne du rôle Owner (concurrence optimiste).
    /// </summary>
    public async Task EnsureAnotherActiveOwnerAsync(StaffMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.EffectiveTenantRole is not { } roleId)
        {
            return;
        }

        var role = await roles.FindAsync(member.TenantId, roleId, cancellationToken);
        if (role is null || !SystemRoles.IsOwnerRole(role))
        {
            return;
        }

        var others = await members.CountActiveTenantWideHoldersAsync(member.TenantId, roleId, member.Id, cancellationToken);
        DomainException.ThrowIf(others == 0, StaffApplicationErrors.LastOwner);
        roles.GuardDecisionsOn(role);
    }

    /// <summary>
    /// Avant de donner <paramref name="role"/> (tenant ou établissement) à <paramref name="target"/> (null : invitation) :
    /// l'appelant détient chaque permission du rôle au niveau du restaurant, seul un Owner donne le rôle Owner.
    /// </summary>
    public async Task EnsureCanGiveRoleAsync(TenantId tenantId, Role role, StaffMember? target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (actor.UserId is not { } actorId)
        {
            return;
        }

        if (target is not null)
        {
            EnsureNotSelf(target, actorId);
        }

        var held = await ActorTenantRoleAsync(tenantId, actorId, cancellationToken);
        if (SystemRoles.IsOwnerRole(role) && !IsOwner(held))
        {
            throw new ForbiddenException(StaffApplicationErrors.OwnerRoleReserved);
        }

        if (role.Permissions.Any(permission => held?.HasPermission(permission) != true))
        {
            throw new ForbiddenException(StaffApplicationErrors.RoleNotHeld);
        }
    }

    /// <summary>Avant de retirer ou remplacer un rôle de <paramref name="target"/> : jamais soi-même, et Owner réservé aux Owners.</summary>
    public async Task EnsureCanTakeRoleAsync(TenantId tenantId, StaffMember target, RoleId? current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (actor.UserId is not { } actorId)
        {
            return;
        }

        EnsureNotSelf(target, actorId);
        await EnsureOwnerActsOnOwnerAsync(tenantId, actorId, [current], cancellationToken);
    }

    /// <summary>Avant de suspendre, réactiver ou retirer <paramref name="target"/> : jamais soi-même, et un Owner (restaurant ou établissement) seulement par un Owner.</summary>
    public async Task EnsureCanChangeMemberAsync(TenantId tenantId, StaffMember target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (actor.UserId is not { } actorId)
        {
            return;
        }

        EnsureNotSelf(target, actorId);
        var held = target.Assignments.Select(assignment => (RoleId?)assignment.RoleId).Prepend(target.TenantWideRoleId);
        await EnsureOwnerActsOnOwnerAsync(tenantId, actorId, held, cancellationToken);
    }

    /// <summary>Avant d'ajouter une permission à un rôle : l'appelant la détient au niveau du restaurant.</summary>
    public async Task EnsureHoldsPermissionAsync(TenantId tenantId, PermissionCode permission, CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } actorId)
        {
            return;
        }

        var held = await ActorTenantRoleAsync(tenantId, actorId, cancellationToken);
        if (held?.HasPermission(permission) != true)
        {
            throw new ForbiddenException(StaffApplicationErrors.PermissionNotHeld);
        }
    }

    // Un des rôles visés est Owner : l'appelant doit être Owner.
    private async Task EnsureOwnerActsOnOwnerAsync(
        TenantId tenantId, UserId actorId, IEnumerable<RoleId?> targetRoles, CancellationToken cancellationToken)
    {
        foreach (var roleId in targetRoles.OfType<RoleId>().Distinct())
        {
            if (await roles.FindAsync(tenantId, roleId, cancellationToken) is { } role && SystemRoles.IsOwnerRole(role))
            {
                if (!IsOwner(await ActorTenantRoleAsync(tenantId, actorId, cancellationToken)))
                {
                    throw new ForbiddenException(StaffApplicationErrors.OwnerRoleReserved);
                }

                return;
            }
        }
    }

    private static void EnsureNotSelf(StaffMember target, UserId actorId)
    {
        if (target.UserId == actorId)
        {
            throw new ForbiddenException(StaffApplicationErrors.CannotChangeOwnRole);
        }
    }

    // Rôle tenant-wide de l'appelant, s'il est membre actif ; null sinon (aucun droit).
    private async Task<Role?> ActorTenantRoleAsync(TenantId tenantId, UserId actorId, CancellationToken cancellationToken) =>
        await members.FindByUserAsync(tenantId, actorId, cancellationToken) is { EffectiveTenantRole: { } roleId }
            ? await roles.FindAsync(tenantId, roleId, cancellationToken)
            : null;

    private static bool IsOwner(Role? role) => role is not null && SystemRoles.IsOwnerRole(role);

    /// <summary>Une affectation à ce rôle et sa suppression concurrente ne peuvent pas passer toutes les deux.</summary>
    public void GuardRole(Role role) => roles.GuardDecisionsOn(role);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

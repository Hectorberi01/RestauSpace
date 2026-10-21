using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

/// <summary>
/// Opération PLATEFORME : donne un propriétaire à un tenant. Crée les rôles système du tenant s'ils manquent (Owner,
/// Manager), trouve ou invite le compte de <paramref name="Email"/> et en fait un propriétaire ACTIF. Le tenant est
/// celui désigné par l'administrateur de la plateforme, pas un tenant de requête. Renvoie l'id du membre du personnel.
/// </summary>
public sealed record OnboardTenantOwnerCommand(Guid TenantId, string? Email) : ICommand<Guid>;

public sealed class OnboardTenantOwnerHandler(
    StaffWorkspace workspace,
    IUserAccounts users,
    IStaffMemberUniquenessChecker memberUniqueness,
    IRoleNameUniquenessChecker roleNameUniqueness,
    KnownPermissions permissions,
    IClock clock)
    : ICommandHandler<OnboardTenantOwnerCommand, Guid>
{
    public async Task<Guid> HandleAsync(OnboardTenantOwnerCommand command, CancellationToken cancellationToken = default)
    {
        if (command.TenantId == Guid.Empty)
        {
            throw new NotFoundException(StaffApplicationErrors.TenantNotFound);
        }

        var tenantId = TenantId.From(command.TenantId);
        await workspace.EnsureWritableAsync(tenantId, cancellationToken);

        var owner = await EnsureSystemRoleAsync(tenantId, SystemRoles.Owner, cancellationToken);
        await EnsureSystemRoleAsync(tenantId, SystemRoles.Manager, cancellationToken);

        var userId = await users.FindOrInviteAsync(command.Email, cancellationToken);
        var isUnique = await memberUniqueness.IsUserUniqueInTenantAsync(tenantId, userId, cancellationToken);
        var member = StaffMember.CreateOwner(tenantId, userId, owner.ToReference(), isUnique, clock);
        workspace.Members.Add(member);

        await workspace.SaveAsync(cancellationToken);
        return member.Id.Value;
    }

    private async Task<Role> EnsureSystemRoleAsync(TenantId tenantId, string name, CancellationToken cancellationToken)
    {
        var existing = await workspace.Roles.FindSystemRoleAsync(tenantId, name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var isUnique = await roleNameUniqueness.IsNameUniqueAsync(tenantId, RoleName.Create(name), null, cancellationToken);
        var role = Role.CreateSystem(
            tenantId, name, SystemRoles.PermissionsOf(name, permissions.Catalog), permissions.Catalog, isUnique, clock);
        workspace.Roles.Add(role);
        return role;
    }
}

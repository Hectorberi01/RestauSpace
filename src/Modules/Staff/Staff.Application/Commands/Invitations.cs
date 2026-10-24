using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

/// <summary>
/// Invite une personne, par email, dans le restaurant courant, éventuellement avec un rôle valable pour tout le tenant.
/// Une personne sans compte reçoit une invitation de compte d'Identity. Renvoie l'id du membre du personnel.
/// </summary>
public sealed record InviteStaffCommand(string? Email, Guid? TenantWideRoleId = null) : ICommand<Guid>;

public sealed class InviteStaffHandler(
    StaffWorkspace workspace, IUserAccounts users, IStaffMemberUniquenessChecker uniqueness, IStaffPlanLimits limits, IClock clock)
    : ICommandHandler<InviteStaffCommand, Guid>
{
    public async Task<Guid> HandleAsync(InviteStaffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        if (await limits.MaxStaffMembersAsync(tenantId, cancellationToken) is { } limit)
        {
            DomainException.ThrowIf(
                await workspace.Members.CountCurrentAsync(tenantId, cancellationToken) >= limit, StaffApplicationErrors.PlanLimitReached);
        }

        var role = command.TenantWideRoleId is { } roleId
            ? await workspace.LoadRoleAsync(tenantId, roleId, cancellationToken)
            : null;
        if (role is not null)
        {
            await workspace.EnsureCanGiveRoleAsync(tenantId, role, null, cancellationToken);
        }

        var userId = await users.FindOrInviteAsync(command.Email, cancellationToken);
        var isUnique = await uniqueness.IsUserUniqueInTenantAsync(tenantId, userId, cancellationToken);
        var member = StaffMember.Invite(tenantId, userId, isUnique, clock);
        if (role is not null)
        {
            member.AssignTenantWide(role.ToReference(), clock);
            workspace.GuardRole(role);
        }

        workspace.Members.Add(member);
        await workspace.SaveAsync(cancellationToken);
        return member.Id.Value;
    }
}

/// <summary>Renvoie l'invitation d'un membre qui ne l'a pas encore acceptée ; l'email part après l'enregistrement.</summary>
public sealed record ResendStaffInvitationCommand(Guid StaffMemberId) : ICommand;

public sealed class ResendStaffInvitationHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<ResendStaffInvitationCommand>
{
    public async Task HandleAsync(ResendStaffInvitationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);

        member.ResendInvitation(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// La personne invitée accepte l'invitation d'un restaurant : son profil devient Active et elle peut ensuite
/// sélectionner ce restaurant. L'utilisateur est celui de l'access token ; aucun restaurant n'a besoin d'être sélectionné.
/// </summary>
public sealed record AcceptStaffInvitationCommand(Guid UserId, Guid TenantId) : ICommand;

public sealed class AcceptStaffInvitationHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<AcceptStaffInvitationCommand>
{
    public async Task HandleAsync(AcceptStaffInvitationCommand command, CancellationToken cancellationToken = default)
    {
        if (command.TenantId == Guid.Empty || command.UserId == Guid.Empty)
        {
            throw new NotFoundException(StaffApplicationErrors.InvitationNotFound);
        }

        var tenantId = TenantId.From(command.TenantId);
        var member = await workspace.Members.FindByUserAsync(tenantId, UserId.From(command.UserId), cancellationToken)
            ?? throw new NotFoundException(StaffApplicationErrors.InvitationNotFound);
        await workspace.EnsureWritableAsync(tenantId, cancellationToken);

        member.Activate(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

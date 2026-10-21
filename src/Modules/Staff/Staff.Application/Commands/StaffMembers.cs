using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

public sealed record AssignStaffToLocationCommand(Guid StaffMemberId, Guid LocationId, Guid RoleId) : ICommand;

public sealed class AssignStaffToLocationHandler(StaffWorkspace workspace, ILocationDirectory locations, IClock clock)
    : ICommandHandler<AssignStaffToLocationCommand>
{
    public async Task HandleAsync(AssignStaffToLocationCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenantId, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(StaffApplicationErrors.LocationNotFound);

        member.AssignToLocation(location, role.ToReference(), clock);
        workspace.GuardRole(role);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveStaffFromLocationCommand(Guid StaffMemberId, Guid LocationId) : ICommand;

public sealed class RemoveStaffFromLocationHandler(StaffWorkspace workspace, IClock clock)
    : ICommandHandler<RemoveStaffFromLocationCommand>
{
    public async Task HandleAsync(RemoveStaffFromLocationCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        if (command.LocationId == Guid.Empty)
        {
            throw new DomainException(StaffMemberErrors.NotAssignedToLocation);
        }

        member.RemoveFromLocation(LocationId.From(command.LocationId), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record AssignTenantWideRoleCommand(Guid StaffMemberId, Guid RoleId) : ICommand;

public sealed class AssignTenantWideRoleHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<AssignTenantWideRoleCommand>
{
    public async Task HandleAsync(AssignTenantWideRoleCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);

        member.AssignTenantWide(role.ToReference(), clock);
        workspace.GuardRole(role);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveTenantWideRoleCommand(Guid StaffMemberId) : ICommand;

public sealed class RemoveTenantWideRoleHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<RemoveTenantWideRoleCommand>
{
    public async Task HandleAsync(RemoveTenantWideRoleCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        await workspace.EnsureAnotherActiveOwnerAsync(member, cancellationToken);

        member.RemoveTenantWide(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record SuspendStaffCommand(Guid StaffMemberId) : ICommand;

public sealed class SuspendStaffHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<SuspendStaffCommand>
{
    public async Task HandleAsync(SuspendStaffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        await workspace.EnsureAnotherActiveOwnerAsync(member, cancellationToken);

        member.Suspend(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ReinstateStaffCommand(Guid StaffMemberId) : ICommand;

public sealed class ReinstateStaffHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<ReinstateStaffCommand>
{
    public async Task HandleAsync(ReinstateStaffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);

        member.Reinstate(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le membre quitte le restaurant (terminal) : affectations et rôle à l'échelle du tenant sont retirés.</summary>
public sealed record RemoveStaffCommand(Guid StaffMemberId) : ICommand;

public sealed class RemoveStaffHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<RemoveStaffCommand>
{
    public async Task HandleAsync(RemoveStaffCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var member = await workspace.LoadMemberAsync(tenantId, command.StaffMemberId, cancellationToken);
        await workspace.EnsureAnotherActiveOwnerAsync(member, cancellationToken);

        member.Leave(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

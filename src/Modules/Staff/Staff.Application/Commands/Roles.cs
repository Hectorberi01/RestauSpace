using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

/// <summary>Crée un rôle personnalisé du restaurant courant, sans permission. Renvoie l'id du rôle.</summary>
public sealed record CreateRoleCommand(string? Name) : ICommand<Guid>;

public sealed class CreateRoleHandler(StaffWorkspace workspace, IRoleNameUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var isUnique = await uniqueness.IsNameUniqueAsync(tenantId, RoleName.Create(command.Name), null, cancellationToken);

        var role = Role.Create(tenantId, command.Name, isUnique, clock);
        workspace.Roles.Add(role);
        await workspace.SaveAsync(cancellationToken);
        return role.Id.Value;
    }
}

public sealed record RenameRoleCommand(Guid RoleId, string? Name) : ICommand;

public sealed class RenameRoleHandler(StaffWorkspace workspace, IRoleNameUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<RenameRoleCommand>
{
    public async Task HandleAsync(RenameRoleCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);
        var isUnique = await uniqueness.IsNameUniqueAsync(tenantId, RoleName.Create(command.Name), role.Id, cancellationToken);

        role.Rename(command.Name, isUnique, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record GrantPermissionCommand(Guid RoleId, string? Permission) : ICommand;

public sealed class GrantPermissionHandler(StaffWorkspace workspace, KnownPermissions permissions, IClock clock)
    : ICommandHandler<GrantPermissionCommand>
{
    public async Task HandleAsync(GrantPermissionCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);

        role.Grant(PermissionCode.Create(command.Permission), permissions.Catalog, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RevokePermissionCommand(Guid RoleId, string? Permission) : ICommand;

public sealed class RevokePermissionHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<RevokePermissionCommand>
{
    public async Task HandleAsync(RevokePermissionCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);

        role.Revoke(PermissionCode.Create(command.Permission), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Supprime un rôle personnalisé qu'aucun membre ne détient plus.</summary>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;

public sealed class DeleteRoleHandler(StaffWorkspace workspace, IClock clock) : ICommandHandler<DeleteRoleCommand>
{
    public async Task HandleAsync(DeleteRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = await workspace.RequireWritableTenantAsync(cancellationToken);
        var role = await workspace.LoadRoleAsync(tenantId, command.RoleId, cancellationToken);

        role.Delete(await workspace.Members.IsRoleHeldAsync(tenantId, role.Id, cancellationToken), clock);
        workspace.Roles.Remove(role);
        await workspace.SaveAsync(cancellationToken);
    }
}

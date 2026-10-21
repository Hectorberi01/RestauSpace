using Staff.Application.Abstractions;

namespace Staff.Application.Commands;

/// <summary>
/// Maintenance de la plateforme, au démarrage : les rôles système (Owner, Manager) de chaque restaurant reçoivent les
/// permissions des modules ajoutés depuis leur création. Ne retire rien. Renvoie le nombre de permissions ajoutées.
/// </summary>
public sealed record CompleteSystemRolesCommand : ICommand<int>;

public sealed class CompleteSystemRolesHandler(IRoleRepository roles, IStaffUnitOfWork unitOfWork, KnownPermissions permissions, IClock clock)
    : ICommandHandler<CompleteSystemRolesCommand, int>
{
    public async Task<int> HandleAsync(CompleteSystemRolesCommand command, CancellationToken cancellationToken = default)
    {
        var added = 0;
        foreach (var role in await roles.ListSystemRolesAsync(cancellationToken))
        {
            if (role.Name.Value is SystemRoles.Owner or SystemRoles.Manager)
            {
                added += role.CompleteSystemPermissions(SystemRoles.PermissionsOf(role.Name.Value, permissions.Catalog), permissions.Catalog, clock);
            }
        }

        if (added > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return added;
    }
}

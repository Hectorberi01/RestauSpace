using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

/// <summary>Ferme définitivement le tenant (terminal).</summary>
public sealed record CloseTenantCommand(Guid TenantId) : ICommand;

public sealed class CloseTenantHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CloseTenantCommand>
{
    public async Task HandleAsync(CloseTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.Close(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

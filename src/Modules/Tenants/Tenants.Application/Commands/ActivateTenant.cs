using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

/// <summary>Met fin à l'essai : Trial -> Active.</summary>
public sealed record ActivateTenantCommand(Guid TenantId) : ICommand;

public sealed class ActivateTenantHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock): ICommandHandler<ActivateTenantCommand>
{
    public async Task HandleAsync(ActivateTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.Activate(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

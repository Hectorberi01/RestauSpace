using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

/// <summary>Suspended -> Active.</summary>
public sealed record ReactivateTenantCommand(Guid TenantId) : ICommand;

public sealed class ReactivateTenantHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ReactivateTenantCommand>
{
    public async Task HandleAsync(ReactivateTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.Reactivate(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

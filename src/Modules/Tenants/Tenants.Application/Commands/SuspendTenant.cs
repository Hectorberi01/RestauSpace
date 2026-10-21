using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

/// <summary>Trial/Active -> Suspended. Déclenché par Billing (impayé, essai expiré) ou par le back-office.</summary>
public sealed record SuspendTenantCommand(Guid TenantId, TenantSuspensionReason Reason) : ICommand;

public sealed class SuspendTenantHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SuspendTenantCommand>
{
    public async Task HandleAsync(SuspendTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.Suspend(command.Reason, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

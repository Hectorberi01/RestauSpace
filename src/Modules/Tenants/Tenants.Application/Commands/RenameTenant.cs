using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

public sealed record RenameTenantCommand(Guid TenantId, string? NewName) : ICommand;

public sealed class RenameTenantHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RenameTenantCommand>
{
    public async Task HandleAsync(RenameTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.Rename(TenantName.Create(command.NewName), clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

/// <summary>Change le fuseau horaire par défaut des FUTURS établissements ; les existants gardent le leur.</summary>
public sealed record ChangeTenantTimeZoneCommand(Guid TenantId, string? TimeZoneId) : ICommand;

public sealed class ChangeTenantTimeZoneHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ChangeTenantTimeZoneCommand>
{
    public async Task HandleAsync(ChangeTenantTimeZoneCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.ChangeTimeZone(TimeZoneId.Create(command.TimeZoneId), clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

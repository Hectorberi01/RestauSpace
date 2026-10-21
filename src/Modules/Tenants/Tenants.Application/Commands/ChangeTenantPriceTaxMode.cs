using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

/// <summary>Bascule les prix de la carte entre TTC et HT, pour les commandes FUTURES.</summary>
public sealed record ChangeTenantPriceTaxModeCommand(Guid TenantId, bool PricesIncludeTax) : ICommand;

public sealed class ChangeTenantPriceTaxModeHandler(ITenantRepository tenants, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ChangeTenantPriceTaxModeCommand>
{
    public async Task HandleAsync(ChangeTenantPriceTaxModeCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        tenant.ChangePriceTaxMode(command.PricesIncludeTax, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

/// <summary>Intègre une nouvelle organisation, en période d'essai. Renvoie l'id du nouveau tenant.</summary>
public sealed record CreateTenantCommand(
    string? Name,
    string? DefaultCurrencyCode,
    string? TimeZoneId,
    bool PricesIncludeTax = true) : ICommand<Guid>;

public sealed class CreateTenantHandler(ITenantRepository tenantsRepository, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateTenantCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = Tenant.Create(
            TenantName.Create(command.Name),
            Currency.FromCode(command.DefaultCurrencyCode),
            TimeZoneId.Create(command.TimeZoneId),
            clock);

        tenant.ChangePriceTaxMode(command.PricesIncludeTax, clock);

        tenantsRepository.Add(tenant);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return tenant.Id.Value;
    }
}

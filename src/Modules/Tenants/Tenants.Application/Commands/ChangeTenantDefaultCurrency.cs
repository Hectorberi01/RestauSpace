using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

/// <summary>Change la devise des documents FUTURS. Refusé dès que le catalogue contient des produits tarifés.</summary>
public sealed record ChangeTenantDefaultCurrencyCommand(Guid TenantId, string? CurrencyCode) : ICommand;

public sealed class ChangeTenantDefaultCurrencyHandler(
    ITenantRepository tenants,
    ITenantPricedCatalogChecker pricedCatalog,
    ITenantsUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<ChangeTenantDefaultCurrencyCommand>
{
    public async Task HandleAsync(ChangeTenantDefaultCurrencyCommand command, CancellationToken cancellationToken = default)
    {
        var currency = Currency.FromCode(command.CurrencyCode);
        var tenant = await tenants.FindAsync(TenantId.From(command.TenantId), cancellationToken);

        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }

        var hasPricedCatalog = currency != tenant.DefaultCurrency && await pricedCatalog.HasPricedCatalogAsync(tenant.Id, cancellationToken);
        tenant.ChangeDefaultCurrency(currency, hasPricedCatalog, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

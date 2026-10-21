namespace Tenants.Domain.Tenants;

/// <summary>
/// Le tenant a-t-il déjà eu au moins un prix au catalogue, d'après la projection locale alimentée par le module Catalog.
/// Sa réponse est l'argument <c>hasPricedCatalog</c> de <see cref="Tenant.ChangeDefaultCurrency"/>.
/// </summary>
public interface ITenantPricedCatalogChecker
{
    Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

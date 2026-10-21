using Catalog.Application.Abstractions;
using Catalog.Application.Queries;
using Catalog.Domain;
using Locations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Commands;
using Tenants.Application.Queries;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

// Les ports déclarés par Catalog, servis par les cas d'usage des autres modules, et la projection « catalogue tarifé »
// tenue par Tenants (par l'outbox). Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Catalog -> Tenants : devise des nouveaux prix, écritures métier autorisées ou non.</summary>
internal sealed class CatalogTenantsFromTenants(CachedTenants tenants) : ICatalogTenants
{
    public async Task<CatalogTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        return tenant is null
            ? null
            : new CatalogTenant(Currency.FromCode(tenant.DefaultCurrencyCode), tenant.CanAcceptBusinessWrites);
    }
}

/// <summary>Catalog -> Locations : un établissement du tenant de la requête, comme référence de menu.</summary>
internal sealed class CatalogLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : ICatalogLocations
{
    public async Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        // La query de Locations répond pour le tenant de la requête : jamais pour un autre.
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new LocationReference(tenantId, locationId, location.IsOperational, location.TimeZoneId);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

internal sealed class PricedCatalogFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("catalog", "CatalogPriced"))
        {
            return;
        }

        var priced = message.Read<SaasRestaurant.Contracts.Catalog.CatalogPriced>();
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPricedCatalogCommand>>()
            .HandleAsync(new RecordPricedCatalogCommand(priced.TenantId), cancellationToken);
    }
}

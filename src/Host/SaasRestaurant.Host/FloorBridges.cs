using Floor.Application.Abstractions;
using Floor.Domain;
using Locations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Floor, servis par les cas d'usage des autres modules. Aucun module n'en référence un autre :
// seul le host les relie.

/// <summary>Floor -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class FloorTenantsFromTenants(CachedTenants tenants) : IFloorTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Floor -> Locations : un établissement du tenant de la requête, comme référence de zone.</summary>
internal sealed class FloorLocationsFromLocations(ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IFloorLocations
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
            return new LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Floor -> Orders : à la fusion de deux tables, les commandes en cours de l'une passent sur l'autre.</summary>
internal sealed class TableSessionOrdersMoverFromOrders(ICommandHandler<Orders.Application.Commands.MoveTableSessionOrdersCommand, int> move)
    : ITableSessionOrdersMover
{
    public Task MoveOpenOrdersAsync(TenantId tenantId, TableSessionId from, TableSessionId to, CancellationToken cancellationToken = default) =>
        move.HandleAsync(new Orders.Application.Commands.MoveTableSessionOrdersCommand(from.Value, to.Value), cancellationToken);
}

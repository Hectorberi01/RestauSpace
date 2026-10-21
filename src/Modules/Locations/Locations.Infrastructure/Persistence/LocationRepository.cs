using Locations.Application.Abstractions;
using Locations.Domain.Locations;

namespace Locations.Infrastructure.Persistence;

/// <summary>
/// Filtre toujours sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.
/// </summary>
internal sealed class LocationRepository(LocationsDbContext context) : ILocationRepository
{
    public Task<RestaurantLocation?> FindAsync(TenantId tenantId, LocationId id, CancellationToken cancellationToken = default) =>
        context.Locations
            .Include(location => location.ExceptionalSchedules)
            .SingleOrDefaultAsync(location => location.TenantId == tenantId && location.Id == id, cancellationToken);

    public async Task<LocationPage> ListAsync(TenantId tenantId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Locations.AsNoTracking().Where(location => location.TenantId == tenantId);
        if (!includeArchived)
        {
            query = query.Where(location => location.Status != LocationStatus.Archived);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(location => location.Name)
            .ThenBy(location => location.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new LocationPage(items, total);
    }

    public void Add(RestaurantLocation location) => context.Locations.Add(location);
}

/// <summary>
/// Page publique : appelée HORS requête de tenant, le query filter masquerait tout ; il est donc contourné ICI SEULEMENT.
/// Lecture seule, par l'id de l'établissement uniquement.
/// </summary>
internal sealed class PublicLocationLookup(LocationsDbContext context) : IPublicLocationLookup
{
    public Task<RestaurantLocation?> FindAsync(LocationId id, CancellationToken cancellationToken = default) =>
        context.Locations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(location => location.Id == id, cancellationToken);
}


using Locations.Application.Abstractions;
using Locations.Domain.Locations;

namespace Locations.Application;

/// <summary>
/// Les étapes communes à toutes les écritures du module : le tenant vient du contexte de requête (jamais de la
/// requête), il doit accepter les écritures métier, l'établissement est chargé dans ce seul tenant, puis la
/// modification est enregistrée.
/// </summary>
public sealed class LocationEditor(
    ITenantContext tenantContext,
    ITenantDirectory tenants,
    ILocationRepository locations,
    ILocationsUnitOfWork unitOfWork)
{
    /// <summary>Le tenant courant, refusé s'il est suspendu, fermé ou inconnu.</summary>
    public async Task<TenantSettings> RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenant();
        var settings = await tenants.FindAsync(tenantId, cancellationToken);
        if (settings is not { AcceptsBusinessWrites: true })
        {
            throw new DomainException(LocationsApplicationErrors.TenantNotWritable);
        }

        return settings;
    }

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public void Add(RestaurantLocation location) => locations.Add(location);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    /// <summary>Charge l'établissement du tenant courant, applique <paramref name="change"/> et enregistre.</summary>
    public async Task EditAsync(Guid locationId, Action<RestaurantLocation> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        var location = await FindAsync(CurrentTenant, locationId, locations, cancellationToken);
        change(location);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Un établissement du tenant, ou 404 (y compris pour un id appartenant à un autre tenant).</summary>
    public static async Task<RestaurantLocation> FindAsync(
        TenantId tenantId, Guid locationId, ILocationRepository locations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var location = locationId == Guid.Empty
            ? null
            : await locations.FindAsync(tenantId, LocationId.From(locationId), cancellationToken);
        return location ?? throw new NotFoundException(LocationsApplicationErrors.NotFound);
    }
}

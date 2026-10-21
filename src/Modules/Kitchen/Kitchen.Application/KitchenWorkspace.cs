using Kitchen.Application.Abstractions;
using Kitchen.Domain;

namespace Kitchen.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class KitchenWorkspace(
    ITenantContext tenantContext,
    IKitchenTenants tenants,
    IKitchenLocations locations,
    IKitchenStationRepository stations,
    IStationRoutingRepository routings,
    IKitchenTicketRepository tickets,
    IKitchenUnitOfWork unitOfWork)
{
    public IKitchenStationRepository Stations => stations;

    public IStationRoutingRepository Routings => routings;

    public IKitchenTicketRepository Tickets => tickets;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(KitchenApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<LocationReference> RequireLocationAsync(TenantId tenantId, Guid locationId, CancellationToken cancellationToken) =>
        (locationId == Guid.Empty ? null : await locations.FindAsync(tenantId, LocationId.From(locationId), cancellationToken))
        ?? throw new NotFoundException(KitchenApplicationErrors.LocationNotFound);

    public async Task<KitchenStation> LoadStationAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await stations.FindAsync(tenantId, StationId.From(id), cancellationToken))
        ?? throw new NotFoundException(KitchenApplicationErrors.StationNotFound);

    public async Task<StationRouting> LoadRoutingAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        await routings.FindByLocationAsync(tenantId, locationId, cancellationToken)
        ?? throw new NotFoundException(KitchenApplicationErrors.RoutingNotFound);

    public async Task<KitchenTicket> LoadTicketAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await tickets.FindAsync(tenantId, KitchenTicketId.From(id), cancellationToken))
        ?? throw new NotFoundException(KitchenApplicationErrors.TicketNotFound);

    /// <summary>Charge le ticket du tenant courant, applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditTicketAsync(Guid id, Action<KitchenTicket> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadTicketAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

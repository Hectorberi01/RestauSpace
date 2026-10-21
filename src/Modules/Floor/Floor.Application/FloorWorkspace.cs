using Floor.Application.Abstractions;
using Floor.Domain;

namespace Floor.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on sauvegarde.
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class FloorWorkspace(
    ITenantContext tenantContext,
    IFloorTenants tenants,
    IDiningAreaRepository diningAreas,
    ITableSessionRepository sessions,
    IFloorUnitOfWork unitOfWork)
{
    public IDiningAreaRepository DiningAreas => diningAreas;

    public ITableSessionRepository Sessions => sessions;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Refuse l'écriture pour un tenant suspendu, fermé ou inconnu.</summary>
    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(FloorApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<DiningArea> LoadDiningAreaAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await diningAreas.FindAsync(tenantId, DiningAreaId.From(id), cancellationToken))
        ?? throw new NotFoundException(FloorApplicationErrors.DiningAreaNotFound);

    /// <summary>La zone qui contient la table ; une table inconnue ou d'un autre tenant donne 404 table.</summary>
    public async Task<DiningArea> LoadDiningAreaOfTableAsync(TenantId tenantId, Guid tableId, CancellationToken cancellationToken) =>
        (tableId == Guid.Empty ? null : await diningAreas.FindByTableAsync(tenantId, TableId.From(tableId), cancellationToken))
        ?? throw new NotFoundException(FloorApplicationErrors.TableNotFound);

    public async Task<TableSession> LoadSessionAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await sessions.FindAsync(tenantId, TableSessionId.From(id), cancellationToken))
        ?? throw new NotFoundException(FloorApplicationErrors.TableSessionNotFound);

    /// <summary>Charge la zone du tenant courant, applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditDiningAreaAsync(Guid id, Action<DiningArea> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadDiningAreaAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public async Task EditSessionAsync(Guid id, Action<TableSession> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadSessionAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

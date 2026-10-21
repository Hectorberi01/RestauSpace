using Floor.Application.Abstractions;
using Floor.Domain;
using Floor.Infrastructure.Persistence.Configurations;

namespace Floor.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class DiningAreaRepository(FloorDbContext context) : IDiningAreaRepository
{
    // Les tables sont toujours chargées dans l'ordre de leur position.
    private IQueryable<DiningArea> Areas => context.DiningAreas
        .Include(area => area.Tables.OrderBy(table => EF.Property<int>(table, RestaurantTableConfiguration.PositionProperty)));

    public Task<DiningArea?> FindAsync(TenantId tenantId, DiningAreaId id, CancellationToken cancellationToken = default) =>
        Areas.SingleOrDefaultAsync(area => area.TenantId == tenantId && area.Id == id, cancellationToken);

    public Task<DiningArea?> FindByTableAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default) =>
        Areas.SingleOrDefaultAsync(
            area => area.TenantId == tenantId && area.Tables.Any(table => table.Id == tableId), cancellationToken);

    public async Task<IReadOnlyList<DiningArea>> ListAsync(
        TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default)
    {
        var query = Areas.AsNoTracking().Where(area => area.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(area => area.LocationId == location);
        }

        return await query.OrderBy(area => area.Name).ThenBy(area => area.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiningArea>> ListForUpdateAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await Areas.Where(area => area.TenantId == tenantId && area.LocationId == locationId)
            .OrderBy(area => area.Name).ThenBy(area => area.Id).ToListAsync(cancellationToken);

    public Task<DiningArea?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default) =>
        Areas.AsNoTracking().SingleOrDefaultAsync(
            area => area.TenantId == tenantId && area.Tables.Any(table => table.QrToken == token), cancellationToken);

    public void Add(DiningArea area) => context.DiningAreas.Add(area);

    public void Remove(DiningArea area) => context.DiningAreas.Remove(area);
}

internal sealed class TableSessionRepository(FloorDbContext context) : ITableSessionRepository
{
    public Task<TableSession?> FindAsync(TenantId tenantId, TableSessionId id, CancellationToken cancellationToken = default) =>
        context.TableSessions.SingleOrDefaultAsync(session => session.TenantId == tenantId && session.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TableSession>> ListOpenAsync(
        TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default)
    {
        var query = context.TableSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.ClosedAtUtc == null);
        if (locationId is { } location)
        {
            query = query.Where(session => session.LocationId == location);
        }

        return await query.OrderBy(session => session.OpenedAtUtc).ThenBy(session => session.Id).ToListAsync(cancellationToken);
    }

    public void Add(TableSession session) => context.TableSessions.Add(session);
}

internal sealed class TableOccupancyChecker(FloorDbContext context) : ITableOccupancyChecker
{
    public Task<bool> HasOpenSessionAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default) =>
        context.TableSessions.AnyAsync(
            session => session.TenantId == tenantId && session.TableId == tableId && session.ClosedAtUtc == null,
            cancellationToken);
}

internal sealed class WaitingPartyRepository(FloorDbContext context) : IWaitingPartyRepository
{
    private static readonly WaitingPartyStatus[] OpenStatuses = [WaitingPartyStatus.Waiting, WaitingPartyStatus.Notified];

    public Task<WaitingParty?> FindAsync(TenantId tenantId, WaitingPartyId id, CancellationToken cancellationToken = default) =>
        context.WaitingParties.SingleOrDefaultAsync(party => party.TenantId == tenantId && party.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WaitingParty>> ListAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken = default) =>
        await context.WaitingParties.AsNoTracking()
            .Where(party => party.TenantId == tenantId && party.LocationId == locationId
                && (OpenStatuses.Contains(party.Status) || party.ClosedAtUtc >= closedSinceUtc))
            .OrderBy(party => party.CreatedAtUtc)
            .ThenBy(party => party.Id)
            .ToListAsync(cancellationToken);

    public Task<int> PurgeClosedAsync(TenantId tenantId, LocationId locationId, DateTimeOffset beforeUtc, CancellationToken cancellationToken = default) =>
        context.WaitingParties
            .Where(party => party.TenantId == tenantId && party.LocationId == locationId && party.ClosedAtUtc != null && party.ClosedAtUtc < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

    public void Add(WaitingParty party) => context.WaitingParties.Add(party);
}

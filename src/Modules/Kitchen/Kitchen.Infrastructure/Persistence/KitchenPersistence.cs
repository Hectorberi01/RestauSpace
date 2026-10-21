using Kitchen.Application.Abstractions;
using Kitchen.Domain;
using Kitchen.Infrastructure.Persistence.Configurations;

namespace Kitchen.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class KitchenStationRepository(KitchenDbContext context) : IKitchenStationRepository
{
    public Task<KitchenStation?> FindAsync(TenantId tenantId, StationId id, CancellationToken cancellationToken = default) =>
        context.Stations.SingleOrDefaultAsync(station => station.TenantId == tenantId && station.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KitchenStation>> FindManyAsync(
        TenantId tenantId, IEnumerable<StationId> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await context.Stations
            .Where(station => station.TenantId == tenantId && wanted.Contains(station.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KitchenStation>> ListAsync(
        TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await context.Stations.AsNoTracking()
            .Where(station => station.TenantId == tenantId && station.LocationId == locationId)
            .OrderBy(station => station.Name)
            .ThenBy(station => station.Id)
            .ToListAsync(cancellationToken);

    public void Add(KitchenStation station) => context.Stations.Add(station);
}

internal sealed class StationRoutingRepository(KitchenDbContext context) : IStationRoutingRepository
{
    public Task<StationRouting?> FindByLocationAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        context.Routings.SingleOrDefaultAsync(routing => routing.TenantId == tenantId && routing.LocationId == locationId, cancellationToken);

    public void Add(StationRouting routing) => context.Routings.Add(routing);
}

internal sealed class KitchenTicketRepository(KitchenDbContext context) : IKitchenTicketRepository
{
    // Les lignes sont toujours chargées dans l'ordre de la commande.
    private IQueryable<KitchenTicket> Tickets => context.Tickets
        .Include(ticket => ticket.Items.OrderBy(item => EF.Property<int>(item, KitchenTicketItemConfiguration.PositionProperty)));

    public Task<KitchenTicket?> FindAsync(TenantId tenantId, KitchenTicketId id, CancellationToken cancellationToken = default) =>
        Tickets.SingleOrDefaultAsync(ticket => ticket.TenantId == tenantId && ticket.Id == id, cancellationToken);

    public Task<KitchenTicket?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        Tickets.SingleOrDefaultAsync(ticket => ticket.TenantId == tenantId && ticket.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<KitchenTicket>> ListOpenAsync(
        TenantId tenantId, LocationId? locationId, StationId? stationId, CancellationToken cancellationToken = default)
    {
        var query = Tickets.AsNoTracking()
            .Where(ticket => ticket.TenantId == tenantId
                && ticket.Status != KitchenTicketStatus.Completed
                && ticket.Status != KitchenTicketStatus.Cancelled);
        if (locationId is { } location)
        {
            query = query.Where(ticket => ticket.LocationId == location);
        }

        if (stationId is { } station)
        {
            query = query.Where(ticket => ticket.Items.Any(item => item.StationId == station));
        }

        return await query
            .OrderByDescending(ticket => ticket.Priority)
            .ThenBy(ticket => ticket.CreatedAtUtc)
            .ThenBy(ticket => ticket.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(KitchenTicket ticket) => context.Tickets.Add(ticket);
}

internal sealed class KitchenStationNameUniquenessChecker(KitchenDbContext context) : IKitchenStationNameUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(
        TenantId tenantId, LocationId locationId, StationName name, StationId? excludedStationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Value.ToUpperInvariant();
        return !await context.Stations
            .Where(station => station.TenantId == tenantId
                && station.LocationId == locationId
                && (excludedStationId == null || station.Id != excludedStationId))
            .AnyAsync(station => EF.Property<string>(station, KitchenStationConfiguration.NormalizedNameProperty) == normalized, cancellationToken);
    }
}

internal sealed class StationRoutingUniquenessChecker(KitchenDbContext context) : IStationRoutingUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        !await context.Routings.AnyAsync(routing => routing.TenantId == tenantId && routing.LocationId == locationId, cancellationToken);
}

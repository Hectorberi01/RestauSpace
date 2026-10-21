using Kitchen.Application.Abstractions;
using Kitchen.Application.Commands;
using Kitchen.Application.Queries;
using Kitchen.Domain;

namespace Kitchen.Application;

/// <summary>
/// Ports en mémoire du module Kitchen. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class KitchenFixture
    : ITenantContext, IKitchenUnitOfWork, IKitchenTenants, IKitchenLocations, IKitchenProducts, IKitchenOrders,
      IKitchenStationNameUniquenessChecker, IStationRoutingUniquenessChecker
{
    public KitchenFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Un établissement du tenant A.</summary>
    public Guid Location { get; }

    /// <summary>Faux module Tenants : true = accepte les écritures métier.</summary>
    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    /// <summary>Faux Catalog : produits par tenant.</summary>
    public Dictionary<ProductId, TenantId> Products { get; } = [];

    /// <summary>Faux module Orders : commandes vues par la cuisine, par tenant.</summary>
    public Dictionary<OrderId, (TenantId Tenant, KitchenOrderToPrepare Order)> Orders { get; } = [];

    public List<KitchenStation> Stations { get; } = [];

    public List<StationRouting> Routings { get; } = [];

    public List<KitchenTicket> Tickets { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public KitchenWorkspace Workspace =>
        new(this, this, this, new StationStore(this), new RoutingStore(this), new TicketStore(this), this);

    public CreateStationHandler CreateStation => new(Workspace, this, Clock);

    public RenameStationHandler RenameStation => new(Workspace, this);

    public ActivateStationHandler ActivateStation => new(Workspace, Clock);

    public DeactivateStationHandler DeactivateStation => new(Workspace, Clock);

    public CreateStationRoutingHandler CreateRouting => new(Workspace, this, Clock);

    public ChangeDefaultStationHandler ChangeDefault => new(Workspace, Clock);

    public SetStationRouteHandler SetRoute => new(Workspace, this, Clock);

    public RemoveStationRouteHandler RemoveRoute => new(Workspace, Clock);

    public CreateTicketForOrderHandler CreateTicket => new(Workspace, this, Clock);

    public CancelTicketForOrderHandler CancelTicketForOrder => new(Workspace, Clock);

    public StartTicketHandler StartTicket => new(Workspace, Clock);

    public StartTicketItemHandler StartItem => new(Workspace, Clock);

    public MarkTicketItemReadyHandler ItemReady => new(Workspace, Clock);

    public MarkTicketReadyHandler TicketReady => new(Workspace, Clock);

    public CompleteTicketHandler Complete => new(Workspace, Clock);

    public CancelTicketHandler CancelTicket => new(Workspace, Clock);

    public ChangeTicketPriorityHandler ChangePriority => new(Workspace, Clock);

    public SetProductCourseHandler SetCourse => new(Workspace, this, Clock);

    public FireCourseHandler FireCourse => new(Workspace, Clock);

    public ListStationsHandler ListStations => new(Workspace);

    public GetStationRoutingHandler GetRouting => new(Workspace);

    public GetTicketByIdHandler GetTicket => new(Workspace);

    public ListOpenTicketsHandler ListOpen => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), IsOperational: true);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    public Guid GivenProduct(TenantId? tenantId = null)
    {
        var id = ProductId.New();
        Products[id] = tenantId ?? TenantA;
        return id.Value;
    }

    /// <summary>Une commande acceptée (par défaut) du tenant A à l'établissement principal, une ligne par produit.</summary>
    public Guid GivenOrder(IReadOnlyList<Guid> products, bool isAccepted = true, Guid? locationId = null, bool isDineIn = false)
    {
        var id = OrderId.New();
        var lines = products
            .Select((product, index) => new KitchenOrderLineToPrepare(
                OrderItemId.New(), ProductId.From(product), $"Plat {index + 1}", null, index + 1, new[] { "sans piment" }, null))
            .ToList();
        Orders[id] = (TenantA, new KitchenOrderToPrepare(id, LocationId.From(locationId ?? Location), isAccepted, lines, isDineIn));
        return id.Value;
    }

    /// <summary>Un poste « Grill » (défaut) et un poste « Bar », routage créé, compteurs remis à zéro.</summary>
    public async Task<(Guid Grill, Guid Bar)> GivenKitchenAsync()
    {
        var grill = await CreateStation.HandleAsync(new CreateStationCommand(Location, "Grill"));
        var bar = await CreateStation.HandleAsync(new CreateStationCommand(Location, "Bar"));
        await CreateRouting.HandleAsync(new CreateStationRoutingCommand(Location, grill));
        SaveCount = 0;
        return (grill, bar);
    }

    public KitchenTicket StoredTicket(Guid id) => Tickets.Single(ticket => ticket.Id.Value == id);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IKitchenTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<LocationReference?> IKitchenLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<bool> IKitchenProducts.ExistsAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken) =>
        Task.FromResult(Products.TryGetValue(productId, out var owner) && owner == tenantId);

    Task<KitchenOrderToPrepare?> IKitchenOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<KitchenOrderToPrepare?>(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Order : null);

    Task<bool> IKitchenStationNameUniquenessChecker.IsUniqueAsync(
        TenantId tenantId, LocationId locationId, StationName name, StationId? excludedStationId, CancellationToken cancellationToken) =>
        Task.FromResult(!Stations.Any(station =>
            station.TenantId == tenantId
            && station.LocationId == locationId
            && station.Id != excludedStationId
            && string.Equals(station.Name.Value, name.Value, StringComparison.OrdinalIgnoreCase)));

    Task<bool> IStationRoutingUniquenessChecker.IsUniqueAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(!Routings.Any(routing => routing.TenantId == tenantId && routing.LocationId == locationId));

    private sealed class StationStore(KitchenFixture fixture) : IKitchenStationRepository
    {
        public Task<KitchenStation?> FindAsync(TenantId tenantId, StationId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KitchenStation?>(fixture.Stations.SingleOrDefault(station => station.TenantId == tenantId && station.Id == id));

        public Task<IReadOnlyList<KitchenStation>> FindManyAsync(
            TenantId tenantId, IEnumerable<StationId> ids, CancellationToken cancellationToken = default)
        {
            var wanted = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<KitchenStation>>(
                fixture.Stations.Where(station => station.TenantId == tenantId && wanted.Contains(station.Id)).ToList());
        }

        public Task<IReadOnlyList<KitchenStation>> ListAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KitchenStation>>(fixture.Stations
                .Where(station => station.TenantId == tenantId && station.LocationId == locationId)
                .OrderBy(station => station.Name.Value, StringComparer.Ordinal)
                .ToList());

        public void Add(KitchenStation station) => fixture.Stations.Add(station);
    }

    private sealed class RoutingStore(KitchenFixture fixture) : IStationRoutingRepository
    {
        public Task<StationRouting?> FindByLocationAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StationRouting?>(fixture.Routings.SingleOrDefault(routing => routing.TenantId == tenantId && routing.LocationId == locationId));

        public void Add(StationRouting routing) => fixture.Routings.Add(routing);
    }

    private sealed class TicketStore(KitchenFixture fixture) : IKitchenTicketRepository
    {
        public Task<KitchenTicket?> FindAsync(TenantId tenantId, KitchenTicketId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KitchenTicket?>(fixture.Tickets.SingleOrDefault(ticket => ticket.TenantId == tenantId && ticket.Id == id));

        public Task<KitchenTicket?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<KitchenTicket?>(fixture.Tickets.SingleOrDefault(ticket => ticket.TenantId == tenantId && ticket.OrderId == orderId));

        public Task<IReadOnlyList<KitchenTicket>> ListOpenAsync(
            TenantId tenantId, LocationId? locationId, StationId? stationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KitchenTicket>>(fixture.Tickets
                .Where(ticket => ticket.TenantId == tenantId && !ticket.IsClosed)
                .Where(ticket => locationId == null || ticket.LocationId == locationId)
                .Where(ticket => stationId == null || ticket.Items.Any(item => item.StationId == stationId))
                .OrderByDescending(ticket => ticket.Priority)
                .ThenBy(ticket => ticket.CreatedAtUtc)
                .ToList());

        public void Add(KitchenTicket ticket) => fixture.Tickets.Add(ticket);
    }
}

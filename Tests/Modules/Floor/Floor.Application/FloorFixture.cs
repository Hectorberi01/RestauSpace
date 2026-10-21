using Floor.Application.Abstractions;
using Floor.Application.Commands;
using Floor.Application.Queries;
using Floor.Domain;

namespace Floor.Application;

/// <summary>
/// Ports en mémoire du module Floor. Comme les vrais repositories, ils ne répondent jamais que dans le tenant qui leur
/// est donné. Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class FloorFixture : ITenantContext, IFloorUnitOfWork, IFloorTenants, IFloorLocations, ITableSessionOrdersChecker
{
    public FloorFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        WritableTenants.Add(TenantA);
        WritableTenants.Add(TenantB);
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Tenants qui acceptent les écritures métier (les autres sont suspendus, fermés ou inconnus).</summary>
    public HashSet<TenantId> WritableTenants { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    /// <summary>Sessions qui ont encore une commande ouverte (réponse simulée du module Orders).</summary>
    public HashSet<TableSessionId> SessionsWithOpenOrders { get; } = [];

    public InMemoryDiningAreas Areas { get; } = new();

    public InMemorySessions Sessions { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public FloorWorkspace Workspace => new(this, this, Areas, Sessions, this);

    public CreateDiningAreaHandler CreateArea => new(Workspace, this, Clock);

    public RenameDiningAreaHandler RenameArea => new(Workspace, Clock);

    public AddTableHandler AddTable => new(Workspace, Clock);

    public RemoveTableHandler RemoveTable => new(Workspace, Sessions, Clock);

    public RenameTableHandler RenameTable => new(Workspace, Clock);

    public PlaceTableHandler PlaceTable => new(Workspace, Clock);

    public DeleteDiningAreaHandler DeleteArea => new(Workspace, Sessions, Clock);

    public SetTableOutOfServiceHandler SetOutOfService => new(Workspace, Clock);

    public OpenTableSessionHandler OpenSession => new(Workspace, Sessions, Clock);

    public ChangeGuestCountHandler ChangeGuestCount => new(Workspace, Clock);

    public CloseTableSessionHandler CloseSession => new(Workspace, this, Clock);

    public GetDiningAreaByIdHandler GetArea => new(Workspace);

    public ListDiningAreasHandler ListAreas => new(Workspace);

    public GetTableSessionByIdHandler GetSession => new(Workspace);

    public ListOpenTableSessionsHandler ListOpenSessions => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), IsOperational: true);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    /// <summary>Une zone avec une table « T1 » de 4 places, dans le tenant donné (A par défaut).</summary>
    public async Task<(Guid Area, Guid Table)> GivenTableAsync(TenantId? tenantId = null, Guid? locationId = null)
    {
        var previous = CurrentTenantId;
        CurrentTenantId = tenantId ?? TenantA;
        try
        {
            var area = await CreateArea.HandleAsync(new CreateDiningAreaCommand(locationId ?? GivenLocation(tenantId), "Terrasse"));
            var table = await AddTable.HandleAsync(new AddTableCommand(area, "T1", 4));
            return (area, table);
        }
        finally
        {
            CurrentTenantId = previous;
            SaveCount = 0;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IFloorTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(WritableTenants.Contains(tenantId));

    Task<LocationReference?> IFloorLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<bool> ITableSessionOrdersChecker.HasOpenOrdersAsync(
        TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken) =>
        Task.FromResult(SessionsWithOpenOrders.Contains(tableSessionId));

    public sealed class InMemoryDiningAreas : IDiningAreaRepository
    {
        public List<DiningArea> Items { get; } = [];

        public DiningArea Stored(Guid id) => Items.Single(area => area.Id.Value == id);

        public Task<DiningArea?> FindAsync(TenantId tenantId, DiningAreaId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<DiningArea?>(Items.SingleOrDefault(area => area.TenantId == tenantId && area.Id == id));

        public Task<DiningArea?> FindByTableAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DiningArea?>(Items.SingleOrDefault(area => area.TenantId == tenantId && area.Tables.Any(table => table.Id == tableId)));

        public Task<IReadOnlyList<DiningArea>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiningArea>>(Items
                .Where(area => area.TenantId == tenantId && (locationId == null || area.LocationId == locationId))
                .OrderBy(area => area.Name, StringComparer.Ordinal)
                .ToList());

        public Task<IReadOnlyList<DiningArea>> ListForUpdateAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiningArea>>(Items.Where(area => area.TenantId == tenantId && area.LocationId == locationId).ToList());

        public Task<DiningArea?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default) =>
            Task.FromResult<DiningArea?>(Items.SingleOrDefault(area => area.TenantId == tenantId && area.Tables.Any(table => table.QrToken == token)));

        public void Add(DiningArea area) => Items.Add(area);

        public void Remove(DiningArea area) => Items.Remove(area);
    }

    public sealed class InMemorySessions : ITableSessionRepository, ITableOccupancyChecker
    {
        public List<TableSession> Items { get; } = [];

        public TableSession Stored(Guid id) => Items.Single(session => session.Id.Value == id);

        public Task<TableSession?> FindAsync(TenantId tenantId, TableSessionId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TableSession?>(Items.SingleOrDefault(session => session.TenantId == tenantId && session.Id == id));

        public Task<IReadOnlyList<TableSession>> ListOpenAsync(
            TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TableSession>>(Items
                .Where(session => session.TenantId == tenantId && session.IsOpen)
                .Where(session => locationId == null || session.LocationId == locationId)
                .OrderBy(session => session.OpenedAtUtc)
                .ToList());

        public void Add(TableSession session) => Items.Add(session);

        public Task<bool> HasOpenSessionAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(session => session.TenantId == tenantId && session.TableId == tableId && session.IsOpen));
    }
}

using Delivery.Application.Abstractions;
using Delivery.Application.Commands;
using Delivery.Application.Queries;
using Delivery.Domain;

namespace Delivery.Application;

/// <summary>
/// Ports en mémoire du module Delivery. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class DeliveryFixture
    : ITenantContext, IDeliveryUnitOfWork, IDeliveryTenants, IDeliveryLocations, IDeliveryOrders, IDeliveryPayments, IDeliveryOperator
{
    public DeliveryFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        WritableTenants.Add(TenantA);
        WritableTenants.Add(TenantB);
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Un établissement du tenant A.</summary>
    public Guid Location { get; }

    public HashSet<TenantId> WritableTenants { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    public List<OrderReference> Orders { get; } = [];

    public InMemorySettings Settings { get; } = new();

    public InMemoryFulfillments Fulfillments { get; } = new();

    public InMemoryCouriers Couriers { get; } = new();

    public InMemoryRemittances Remittances { get; } = new();

    /// <summary>Reste à payer par commande (zéro par défaut : commande réglée).</summary>
    public Dictionary<Guid, decimal> AmountsDue { get; } = [];

    /// <summary>Encaissements par le livreur acceptés par les établissements.</summary>
    public HashSet<CollectionMethod> AcceptedCollections { get; } = [CollectionMethod.Cash, CollectionMethod.MobileMoney];

    public Guid? CurrentUserId { get; set; } = Guid.NewGuid();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public DeliveryWorkspace Workspace => new(this, this, this, Settings, Fulfillments, Couriers, Remittances, this, Clock);

    public ConfigurePickupHandler ConfigurePickup => new(Workspace, Clock);

    public ConfigureDeliveryHandler ConfigureDelivery => new(Workspace, Clock);

    public AddDeliveryZoneHandler AddZone => new(Workspace, Clock);

    public SetDeliveryZoneActiveHandler SetZoneActive => new(Workspace, Clock);

    public CreatePickupHandler CreatePickup => new(Workspace, this, Clock);

    public CreateDeliveryHandler CreateDelivery => new(Workspace, this, Clock);

    public AdvanceFulfillmentHandler Advance => new(Workspace, this, Clock);

    public AssignCourierHandler AssignCourier => new(Workspace, Clock);

    public ConfirmDeliveryHandler ConfirmDelivery => new(Workspace, this, Clock);

    public CreateCourierHandler CreateCourier => new(Workspace, Clock);

    public UpdateCourierHandler UpdateCourier => new(Workspace, Clock);

    public SetCourierActiveHandler SetCourierActive => new(Workspace, Clock);

    public RecordRemittanceHandler RecordRemittance => new(Workspace, this, Clock);

    public ListCouriersHandler ListCouriers => new(Workspace);

    public GetCourierBalancesHandler Balances => new(Workspace);

    public ListRemittancesHandler ListRemittances => new(Workspace);

    public GetRemittanceHandler GetRemittance => new(Workspace);

    public MarkOrderReadyForPickupHandler OrderReady => new(Workspace, Clock);

    public CancelOrderFulfillmentHandler OrderCancelled => new(Workspace, Clock);

    public GetDeliverySettingsHandler GetSettings => new(Workspace);

    public GetFulfillmentHandler GetFulfillment => new(Workspace);

    public GetOrderFulfillmentHandler GetOrderFulfillment => new(Workspace);

    public ListFulfillmentsHandler List => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), IsOperational: true);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    public Guid GivenOrder(OrderKind kind, decimal total = 10000, TenantId? tenantId = null, Guid? locationId = null, bool isOpen = true)
    {
        var order = new OrderReference(
            tenantId ?? TenantA, OrderId.New(), LocationId.From(locationId ?? Location), kind, Money.Of(total, Currency.XOF), isOpen);
        Orders.Add(order);
        return order.OrderId.Value;
    }

    /// <summary>Livraison activée avec une zone « Centre » (frais 1000, minimum 5000, 25 min) ; compteur remis à zéro.</summary>
    public async Task<Guid> GivenDeliveryZoneAsync()
    {
        var zone = await AddZone.HandleAsync(new AddDeliveryZoneCommand(Location, "Centre", 1000, 5000, 25));
        await ConfigureDelivery.HandleAsync(new ConfigureDeliveryCommand(Location, true, 30));
        SaveCount = 0;
        return zone;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IDeliveryTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(WritableTenants.Contains(tenantId));

    Task<Currency> IDeliveryTenants.CurrencyOfAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Currency.XOF);

    Task<LocationReference?> IDeliveryLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<OrderReference?> IDeliveryOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<OrderReference?>(Orders.SingleOrDefault(order => order.TenantId == tenantId && order.OrderId == orderId));

    Task<Money> IDeliveryPayments.AmountDueAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Money.Of(AmountsDue.GetValueOrDefault(orderId.Value), Currency.XOF));

    Task<bool> IDeliveryPayments.AcceptsAsync(
        TenantId tenantId, LocationId locationId, CollectionMethod method, Money amount, CancellationToken cancellationToken) =>
        Task.FromResult(AcceptedCollections.Contains(method));

    public sealed class InMemoryCouriers : ICourierRepository
    {
        public List<Courier> Items { get; } = [];

        public Task<Courier?> FindAsync(TenantId tenantId, CourierId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Courier?>(Items.SingleOrDefault(courier => courier.TenantId == tenantId && courier.Id == id));

        public Task<IReadOnlyList<Courier>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Courier>>(Items
                .Where(courier => courier.TenantId == tenantId && (includeInactive || courier.IsActive))
                .OrderBy(courier => courier.Name, StringComparer.Ordinal)
                .ToList());

        public void Add(Courier courier) => Items.Add(courier);
    }

    public sealed class InMemoryRemittances : ICourierRemittanceRepository
    {
        public List<CourierRemittance> Items { get; } = [];

        public Task<CourierRemittance?> FindAsync(TenantId tenantId, CourierRemittanceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CourierRemittance?>(Items.SingleOrDefault(remittance => remittance.TenantId == tenantId && remittance.Id == id));

        public Task<IReadOnlyList<CourierRemittance>> SearchAsync(
            TenantId tenantId,
            LocationId? locationId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int skip,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CourierRemittance>>(Items
                .Where(remittance => remittance.TenantId == tenantId && (locationId is null || remittance.LocationId == locationId))
                .OrderByDescending(remittance => remittance.RecordedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToList());

        public void Add(CourierRemittance remittance) => Items.Add(remittance);
    }

    public sealed class InMemorySettings : IDeliverySettingsRepository
    {
        public List<DeliverySettings> Items { get; } = [];

        public Task<DeliverySettings?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeliverySettings?>(Items.SingleOrDefault(settings => settings.TenantId == tenantId && settings.LocationId == locationId));

        public void Add(DeliverySettings settings) => Items.Add(settings);
    }

    public sealed class InMemoryFulfillments : IFulfillmentRepository
    {
        public List<Fulfillment> Items { get; } = [];

        public Fulfillment Stored(Guid id) => Items.Single(fulfillment => fulfillment.Id.Value == id);

        public Task<Fulfillment?> FindAsync(TenantId tenantId, FulfillmentId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Fulfillment?>(Items.SingleOrDefault(fulfillment => fulfillment.TenantId == tenantId && fulfillment.Id == id));

        public Task<Fulfillment?> FindActiveForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Fulfillment?>(Items.SingleOrDefault(fulfillment =>
                fulfillment.TenantId == tenantId && fulfillment.OrderId == orderId && fulfillment.Status != FulfillmentStatus.Cancelled));

        public Task<IReadOnlyList<Fulfillment>> SearchAsync(
            TenantId tenantId, FulfillmentSearch search, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Fulfillment>>(Items
                .Where(fulfillment => fulfillment.TenantId == tenantId)
                .Where(fulfillment => search.LocationId is null || fulfillment.LocationId == search.LocationId)
                .Where(fulfillment => search.Mode is null || fulfillment.Mode == search.Mode)
                .Where(fulfillment => search.Status is null || fulfillment.Status == search.Status)
                .Where(fulfillment => !search.OpenOnly || !fulfillment.IsClosed)
                .OrderBy(fulfillment => fulfillment.PromisedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToList());

        public Task<IReadOnlyList<Fulfillment>> ListAwaitingRemittanceAsync(
            TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Fulfillment>>(Items
                .Where(fulfillment => fulfillment.TenantId == tenantId && (locationId is null || fulfillment.LocationId == locationId) && fulfillment.AwaitsRemittance)
                .ToList());

        public Task<IReadOnlyList<Fulfillment>> ListForUpdateAsync(
            TenantId tenantId, IReadOnlyCollection<FulfillmentId> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Fulfillment>>(Items.Where(fulfillment => fulfillment.TenantId == tenantId && ids.Contains(fulfillment.Id)).ToList());

        public void Add(Fulfillment fulfillment) => Items.Add(fulfillment);
    }
}

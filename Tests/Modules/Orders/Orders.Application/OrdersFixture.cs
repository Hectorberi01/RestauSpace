using Orders.Application.Abstractions;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;

namespace Orders.Application;

/// <summary>
/// Ports en mémoire du module Orders. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; tenant A en francs CFA TTC, tenant B en euros HT.
/// </summary>
public sealed class OrdersFixture
    : ITenantContext, IOrdersUnitOfWork, IOrderTenants, IOrderLocations, IOrderTableSessions, IOrderCatalog, IOrderPayments, IOrderNumberGenerator, IOrderCustomers, IOrderPlanLimits
{
    public OrdersFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Tenants[TenantA] = new OrderTenant(Currency.XOF, PriceTaxMode.TaxInclusive, AcceptsBusinessWrites: true);
        Tenants[TenantB] = new OrderTenant(Currency.EUR, PriceTaxMode.TaxExclusive, AcceptsBusinessWrites: true);
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Un établissement actif du tenant A.</summary>
    public Guid Location { get; }

    public Dictionary<TenantId, OrderTenant> Tenants { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    /// <summary>Établissements gelés (au-delà de la limite du plan), selon le faux module Locations.</summary>
    public HashSet<LocationId> FrozenLocations { get; } = [];

    public List<TableSessionReference> Sessions { get; } = [];

    /// <summary>Produits vendables du faux Catalog, par id : (tenant, nom, prix unitaire, taux de taxe). Chaque option choisie coûte 5.</summary>
    public Dictionary<ProductId, (TenantId Tenant, string Name, decimal UnitPrice, decimal TaxRate)> Products { get; } = [];

    /// <summary>Montant réglé par commande (faux module Payments) ; zéro par défaut.</summary>
    public Dictionary<OrderId, decimal> Paid { get; } = [];

    public InMemoryOrders Orders { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    private int _lastNumber;

    public OrdersWorkspace Workspace => new(this, this, Orders, this);

    public CreateOrderHandler Create => new(Workspace, this, this, this, this, Clock);

    public AddOrderItemHandler AddItem => new(Workspace, this);

    public ChangeOrderItemQuantityHandler ChangeQuantity => new(Workspace);

    public ChangeOrderItemNoteHandler ChangeNote => new(Workspace);

    public RemoveOrderItemHandler RemoveItem => new(Workspace);

    public ApplyManualDiscountHandler ApplyDiscount => new(Workspace);

    public RemoveDiscountHandler RemoveDiscount => new(Workspace);

    public ApplyExternalDiscountHandler ApplyExternalDiscount => new(Workspace);

    public ApplyDeliveryFeeHandler ApplyDeliveryFee => new(Workspace, Clock);

    public PlaceOrderHandler Place => new(Workspace, this, this, Clock);

    /// <summary>Plafond mensuel de commandes du plan (null : aucun).</summary>
    public long? MonthlyOrderLimit { get; set; }

    public AcceptOrderHandler Accept => new(Workspace, Clock);

    public StartOrderPreparationHandler StartPreparation => new(Workspace, Clock);

    public MarkOrderReadyHandler MarkReady => new(Workspace, Clock);

    public CompleteOrderHandler Complete => new(Workspace, this, Clock);

    public CancelOrderHandler Cancel => new(Workspace, Clock);

    public ChangeOrderCustomerHandler ChangeCustomer => new(Workspace, this, Clock);

    public GetOrderByIdHandler GetById => new(Workspace);

    public QuoteOrderHandler Quote => new(Workspace, this, this, Clock);

    public ListOrdersHandler List => new(Workspace);

    public HasOpenOrdersForTableSessionHandler HasOpenOrders => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null, bool isOperational = true)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), isOperational);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    public Guid GivenProduct(string name = "Poulet DG", decimal unitPrice = 3500m, decimal taxRate = 0.18m, TenantId? tenantId = null)
    {
        var id = ProductId.New();
        Products[id] = (tenantId ?? TenantA, name, unitPrice, taxRate);
        return id.Value;
    }

    /// <summary>Fiches du faux module Customers : (tenant, anonymisée ?).</summary>
    public Dictionary<CustomerId, (TenantId Tenant, bool IsAnonymized)> Customers { get; } = [];

    public Guid GivenCustomer(bool isAnonymized = false, TenantId? tenantId = null)
    {
        var id = CustomerId.New();
        Customers[id] = (tenantId ?? TenantA, isAnonymized);
        return id.Value;
    }

    public Guid GivenSession(bool isOpen = true, Guid? locationId = null)
    {
        var session = new TableSessionReference(TenantA, TableSessionId.New(), LocationId.From(locationId ?? Location), isOpen);
        Sessions.Add(session);
        return session.TableSessionId.Value;
    }

    /// <summary>Brouillon sur place du tenant A avec une ligne (2 x 3 500 XOF TTC, taxe 18 %), compteurs remis à zéro.</summary>
    public async Task<(Guid Order, Guid Item)> GivenDraftAsync(Guid? tableSession = null)
    {
        var order = await Create.HandleAsync(new CreateOrderCommand(Location, OrderType.DineIn, tableSession));
        var item = await AddItem.HandleAsync(new AddOrderItemCommand(order, GivenProduct(), 2));
        SaveCount = 0;
        return (order, item);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<OrderTenant?> IOrderTenants.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<OrderTenant?>(Tenants.TryGetValue(tenantId, out var tenant) ? tenant : null);

    Task<LocationReference?> IOrderLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<bool> IOrderLocations.IsFrozenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(FrozenLocations.Contains(locationId));

    Task<long?> IOrderPlanLimits.MonthlyOrdersAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult(MonthlyOrderLimit);

    Task<CustomerReference?> IOrderCustomers.FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(Customers.TryGetValue(customerId, out var customer) && customer.Tenant == tenantId
            ? new CustomerReference(tenantId, customerId, customer.IsAnonymized)
            : null);

    Task<TableSessionReference?> IOrderTableSessions.FindAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<TableSessionReference?>(Sessions.SingleOrDefault(session => session.TenantId == tenantId && session.TableSessionId == sessionId));

    /// <summary>Supplément de chaque composant décrit par le faux catalogue.</summary>
    public decimal ComponentSupplement { get; set; }

    public IReadOnlyList<ComponentChoice>? LastComponents { get; private set; }

    /// <summary>Établissement et canal de la dernière vente décrite par le faux catalogue.</summary>
    public (LocationId Location, OrderChannel Channel)? LastSale { get; private set; }

    Task<ProductSnapshot?> IOrderCatalog.DescribeForSaleAsync(
        TenantId tenantId, ProductId productId, VariantId? variantId, IReadOnlyList<ModifierId> modifierIds, LocationId locationId, OrderChannel channel,
        IReadOnlyList<ComponentChoice>? components, CancellationToken cancellationToken)
    {
        LastSale = (locationId, channel);
        if (!Products.TryGetValue(productId, out var product) || product.Tenant != tenantId)
        {
            return Task.FromResult<ProductSnapshot?>(null);
        }

        var currency = Tenants[tenantId].Currency;
        var modifiers = modifierIds.Select((id, index) => OrderItemModifier.Create(id, $"Option {index + 1}", Money.Of(5m, currency)));
        LastComponents = components;
        var chosen = (components ?? []).Select(component => OrderItemComponent.Create(
            null, "Plat", component.ProductId, Products[component.ProductId].Name, null, null, Money.Of(ComponentSupplement, currency),
            component.ModifierIds.Select((id, index) => OrderItemModifier.Create(id, $"Option {index + 1}", Money.Of(1m, currency)))));
        return Task.FromResult<ProductSnapshot?>(ProductSnapshot.Create(
            tenantId, productId, product.Name, Money.Of(product.UnitPrice, currency),
            TaxSnapshot.Create("TVA", "TVA", product.TaxRate), modifiers, components: chosen));
    }

    Task<Money> IOrderPayments.SettledAmountAsync(TenantId tenantId, OrderId orderId, Currency currency, CancellationToken cancellationToken) =>
        Task.FromResult(Money.Of(Paid.GetValueOrDefault(orderId), currency));

    Task<OrderNumber> IOrderNumberGenerator.NextAsync(TenantId tenantId, LocationId locationId, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(OrderNumber.Create($"{day:yyyyMMdd}-{++_lastNumber:D3}"));

    public sealed class InMemoryOrders : IOrderRepository
    {
        public List<Order> Items { get; } = [];

        public Order Stored(Guid id) => Items.Single(order => order.Id.Value == id);

        public Task<Order?> FindAsync(TenantId tenantId, OrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Order?>(Items.SingleOrDefault(order => order.TenantId == tenantId && order.Id == id));

        public Task<OrderPage> ListAsync(TenantId tenantId, OrderSearch search, int skip, int take, CancellationToken cancellationToken = default)
        {
            var matching = Items
                .Where(order => order.TenantId == tenantId)
                .Where(order => search.CustomerId == null || order.CustomerId == search.CustomerId)
                .Where(order => search.LocationId == null || order.LocationId == search.LocationId)
                .Where(order => search.Status == null || order.Status == search.Status)
                .Where(order => search.TableSessionId == null || order.TableSessionId == search.TableSessionId)
                .Where(order => !search.ExcludeDrafts || order.Status != OrderStatus.Draft)
                .Where(order => search.Type == null || order.Type == search.Type)
                .Where(order => search.Channel == null || order.Channel == search.Channel)
                .Where(order => search.FromUtc == null || order.CreatedAtUtc >= search.FromUtc)
                .Where(order => search.ToUtc == null || order.CreatedAtUtc < search.ToUtc)
                .Where(order => search.MinTotal == null || order.Total.Amount >= search.MinTotal)
                .Where(order => search.MaxTotal == null || order.Total.Amount <= search.MaxTotal)
                .Where(order => search.Number is not { } number
                    || (number.IsSuffix ? order.Number.Value.EndsWith(number.Value, StringComparison.Ordinal) : order.Number.Value == number.Value))
                .OrderByDescending(order => order.CreatedAtUtc)
                .ToList();
            return Task.FromResult(new OrderPage(matching.Skip(skip).Take(take).ToList(), matching.Count));
        }

        public Task<bool> HasOpenOrdersAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(order => order.TenantId == tenantId && order.TableSessionId == tableSessionId && !order.IsFinalized));

        public Task<IReadOnlyList<Order>> ListOpenByTableSessionAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Order>>(Items.Where(order => order.TenantId == tenantId && order.TableSessionId == tableSessionId && !order.IsFinalized).ToList());

        public Task<int> CountPlacedAsync(TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Count(order => order.TenantId == tenantId && order.PlacedAtUtc >= fromUtc && order.PlacedAtUtc < toUtc));

        public Task<IReadOnlyList<CustomerId>> ListCustomerIdsAsync(TenantId tenantId, LocationId locationId, int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerId>>(Items
                .Where(order => order.TenantId == tenantId && order.LocationId == locationId && order.CustomerId is not null && order.Status != OrderStatus.Cancelled)
                .Select(order => order.CustomerId!.Value)
                .Distinct()
                .Take(max)
                .ToList());

        public void Add(Order order) => Items.Add(order);
    }
}

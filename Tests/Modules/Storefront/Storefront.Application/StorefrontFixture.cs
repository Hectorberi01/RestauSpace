using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Kiosks;
using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application;

/// <summary>Le module en mémoire : deux restaurants, leurs établissements, leurs options, et l'application de la requête.</summary>
public sealed class StorefrontFixture
    : ITenantContext, IStorefrontTenants, ISalesAppRepository, ISalesAppKeyLookup, IOnlineOrderingSettingsRepository, IStorefrontLocations,
      IStorefrontEntitlements, IStorefrontClient, IStorefrontCatalog, IStorefrontOrders, IStorefrontCustomers, IStorefrontPayments, IOnlineOrderRepository, IStorefrontTickets,
      IKioskDeviceRepository, IKioskDeviceLookup, IKioskPairingRepository, IStorefrontDelivery, IStorefrontCoupons, IStorefrontOtp, IStorefrontLoyalty,
      ICustomerSessionRepository, ICustomerSessionLookup, IStorefrontUnitOfWork, IStorefrontTables
{
    public StorefrontFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        CurrentTenantId = TenantA;
        Restaurants[TenantA] = new StorefrontRestaurant("Chez Awa", null, "#16a34a", "XOF", true, true);
        Restaurants[TenantB] = new StorefrontRestaurant("Chez B", null, null, "EUR", true, true);
        Cotonou = Location(TenantA, "Cotonou", open: true);
        Calavi = Location(TenantA, "Calavi", open: false);
        Paris = Location(TenantB, "Paris", open: true);
        Options.Add((TenantA, SalesAppKind.Web));
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public LocationId Cotonou { get; }

    public LocationId Calavi { get; }

    public LocationId Paris { get; }

    public Dictionary<TenantId, StorefrontRestaurant> Restaurants { get; } = [];

    public List<(TenantId Tenant, StorefrontLocation Location)> Locations { get; } = [];

    public HashSet<(TenantId, SalesAppKind)> Options { get; } = [];

    public List<SalesApp> Apps { get; } = [];

    public List<OnlineOrderingSettings> Settings { get; } = [];

    public SalesAppId? AppId { get; set; }

    public KioskDeviceId? DeviceId { get; set; }

    public LocationId? DeviceLocationId { get; set; }

    public CustomerSessionId? CustomerSessionId { get; set; }

    public CustomerId? CustomerId { get; set; }

    /// <summary>Nombre de bornes de l'option ; null = sans limite.</summary>
    public long? KioskLimit { get; set; }

    public List<KioskDevice> Kiosks { get; } = [];

    public List<KioskPairing> Pairings { get; } = [];

    /// <summary>Les tickets partis vers l'imprimante : (établissement, référence, ticket).</summary>
    public List<(LocationId Location, string Reference, KioskTicket Ticket)> Tickets { get; } = [];

    /// <summary>Faux : aucune imprimante de tickets client dans l'établissement.</summary>
    public bool HasTicketPrinter { get; set; } = true;

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public StorefrontWorkspace Workspace => new(this, this, this, this, this, this, this);

    public CreateSalesAppHandler CreateApp => new(Workspace, Clock);

    public UpdateSalesAppHandler UpdateApp => new(Workspace, Clock);

    public RotateSalesAppKeyHandler RotateKey => new(Workspace, Clock);

    public DisableSalesAppHandler DisableApp => new(Workspace, Clock);

    public EnableSalesAppHandler EnableApp => new(Workspace, Clock);

    public SaveOnlineOrderingSettingsHandler SaveSettings => new(Workspace, Clock);

    public PauseOnlineOrderingHandler Pause => new(Workspace, Clock);

    public GetStorefrontOverviewHandler Overview => new(Workspace, Clock);

    public AuthenticatePublishableKeyHandler Authenticate => new(this, this, this, this, Clock);

    public StartKioskPairingHandler StartPairing => new(Workspace, this, this, this, Clock);

    public ClaimKioskPairingHandler ClaimPairing => new(Workspace, this, this, this, this, Clock);

    public ApproveKioskPairingHandler ApprovePairing => new(Workspace, this, this, Clock);

    public ListKiosksHandler ListKiosks => new(Workspace, this);

    public RevokeKioskHandler RevokeKiosk => new(Workspace, this, Clock);

    public GetStorefrontHandler Storefront => new(Workspace, this, this, Clock);

    public GetStorefrontLocationHandler StorefrontLocation => new(Workspace, this, this, Clock);

    public GetStorefrontCatalogHandler Catalog => new(Workspace, this, this, Clock);

    public OrderTrackingTokens Tokens { get; } = new(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public QuoteCartHandler Quote => new(Workspace, this, this, this, this, this, this, Clock);

    public PlaceGuestOrderHandler Place => new(Workspace, this, this, this, this, this, this, this, this, this, Tokens, this, this, Clock, Limits);

    public CheckoutLimits Limits { get; } = new();

    /// <summary>Empreinte de l'adresse IP de la requête ; null = inconnue.</summary>
    public string? ClientKey { get; set; } = "ip-1";

    /// <summary>Tables du faux module Floor, par jeton de QR ; les sessions ouvertes par table.</summary>
    public Dictionary<string, (TenantId Tenant, StorefrontTable Table)> Tables { get; } = [];

    public Dictionary<Guid, TableSessionId> SeatedTables { get; } = [];

    Task<StorefrontTable?> IStorefrontTables.FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken) =>
        Task.FromResult(Tables.TryGetValue(token, out var table) && table.Tenant == tenantId ? table.Table : null);

    Task<TableSessionId> IStorefrontTables.SeatAsync(TenantId tenantId, Guid tableId, CancellationToken cancellationToken)
    {
        if (!SeatedTables.TryGetValue(tableId, out var session))
        {
            session = TableSessionId.New();
            SeatedTables[tableId] = session;
        }

        return Task.FromResult(session);
    }

    // ---- Comptes clients (faux Notifications, Customers et Loyalty)

    public Storefront.Application.Customers.CustomerSignInTickets SignInTickets { get; } = new(Enumerable.Range(40, 32).Select(i => (byte)i).ToArray());

    public Storefront.Application.Customers.RequestCustomerCodeHandler RequestCode => new(Workspace, this, this, SignInTickets);

    public Storefront.Application.Customers.VerifyCustomerCodeHandler VerifyCode => new(Workspace, this, this, this, this, SignInTickets, this, this, Clock);

    public Storefront.Application.Customers.SignOutCustomerHandler SignOut => new(Workspace, this, this, this, Clock);

    public Storefront.Application.Customers.GetCustomerAccountHandler Account => new(Workspace, this, this, this);

    public Storefront.Application.Customers.ListCustomerOrdersHandler CustomerOrders => new(Workspace, this, this, this, this, Tokens, Clock);

    /// <summary>Le seul bon code des faux défis.</summary>
    public const string GoodCode = "123456";

    /// <summary>Les codes envoyés : défi, canal, adresse, et s'il a déjà servi.</summary>
    public List<(Guid Challenge, CustomerSignInChannel Channel, string Address, bool Used)> SentCodes { get; } = [];

    /// <summary>Fiches clients : nom et coordonnées ; retirée = anonymisée.</summary>
    public Dictionary<Guid, CustomerProfile> Profiles { get; } = [];

    public List<CustomerSession> Sessions { get; } = [];

    public bool LoyaltyEnabled { get; set; } = true;

    public Dictionary<Guid, long> Balances { get; } = [];

    public List<(Guid Id, string Name, int Cost, decimal Discount)> Rewards { get; } = [];

    public List<Guid> ConsentsGranted { get; } = [];

    public GetGuestOrderHandler Track => new(Workspace, this, this, this, this, Tokens, Clock);

    public ListSlotsHandler Slots => new(Workspace, this, this, this, Clock);

    // ---- Livraison et coupons (faux modules Delivery et Promotions)

    /// <summary>Zones livrées par établissement ; absent = l'établissement ne livre pas.</summary>
    public Dictionary<LocationId, StorefrontDeliveryOffer> DeliveryOffers { get; } = [];

    /// <summary>Les fiches de livraison créées à la validation.</summary>
    public Dictionary<OrderId, DeliveryToPlace> Deliveries { get; } = [];

    /// <summary>Coupons par code : promotion, remise fixe, ou motif de refus.</summary>
    public Dictionary<string, (Guid PromotionId, decimal Amount, string? Refusal)> Coupons { get; } = [];

    /// <summary>Les coupons comptés à la validation (code, commande).</summary>
    public List<(string Code, OrderId Order)> RedeemedCoupons { get; } = [];

    public Dictionary<OrderId, (decimal Discount, decimal? Fee)> OrderAdjustments { get; } = [];

    public CancelGuestOrderHandler Cancel => new(Workspace, this, this, this, Tokens, this, Clock);

    public ResolveOverdueOnlineOrderHandler Resolve => new(Workspace, this, this, this, this, Clock);

    public StartGuestPaymentHandler StartPayment => new(Workspace, this, this, this, this, Tokens, Clock);

    public RecordOnlinePaymentHandler RecordPayment => new(Workspace, this, this, this, this, this, Clock);

    /// <summary>Prestataires en ligne configurés, par tenant.</summary>
    public Dictionary<TenantId, List<OnlinePaymentOption>> PaymentOptions { get; } = [];

    /// <summary>Faux module Payments : les paiements de chaque commande.</summary>
    public List<(Guid Id, OrderId Order, string Status, decimal Amount, Uri? ReturnUrl)> Payments { get; } = [];

    /// <summary>Le prestataire confirme le paiement en attente de la commande (webhook).</summary>
    public void Capture(OrderId orderId)
    {
        var index = Payments.FindIndex(payment => payment.Order == orderId && payment.Status == "Pending");
        Payments[index] = Payments[index] with { Status = "Captured" };
    }

    /// <summary>Prix unitaire par produit ; un produit absent est refusé comme par Catalog.</summary>
    public Dictionary<Guid, decimal> Prices { get; } = [];

    /// <summary>Les commandes créées par Orders : statut et contenu.</summary>
    public Dictionary<OrderId, (TenantId Tenant, OrderToPlace Order, GuestOrderStatus Status, decimal Total, string? CancelReason)> Orders { get; } = [];

    /// <summary>Fiches clients (faux Customers) : tenant, id, e-mail et téléphone.</summary>
    public List<(TenantId Tenant, Guid Id, string? Email, string? Phone)> Customers { get; } = [];

    /// <summary>Les fiches créées dont le consentement marketing a été enregistré à la création.</summary>
    public List<Guid> ConsentsAtCreation { get; } = [];

    /// <summary>La seule fiche du tenant A qui porte exactement ces contacts.</summary>
    public Guid CustomerWith(string? email, string? phone) =>
        Customers.Single(customer => customer.Tenant == TenantA && customer.Email == email && customer.Phone == phone).Id;

    public List<GuestCustomer> CustomerRequests { get; } = [];

    public List<OnlineOrder> OnlineOrders { get; } = [];

    /// <summary>Faux : Orders refuse la validation (plafond du plan, par exemple).</summary>
    public bool OrdersRefusePlacing { get; set; }

    public static readonly DomainError PlacingRefused = new("orders.plan.monthly_limit_reached", "Limit.");

    /// <summary>Catalogue par (tenant, établissement, canal) ; absent = vide.</summary>
    public Dictionary<(TenantId, LocationId, SalesAppKind), StorefrontCatalogContent> Catalogs { get; } = [];

    private LocationId Location(TenantId tenant, string name, bool open)
    {
        var id = LocationId.New();
        Locations.Add((tenant, new StorefrontLocation(
            id, name, "Rue 1", name, "Africa/Porto-Novo", true, open, open ? null : Clock.UtcNow.AddHours(2),
            [new StorefrontOpeningSlot(DayOfWeek.Monday, "11:00", "22:00", false)], null)));
        return id;
    }

    /// <summary>Joue une requête publique : le tenant et l'application viennent de la clé authentifiée.</summary>
    public async Task<StorefrontKeyPrincipal?> SignInAsync(string key, string? origin = null, string? deviceToken = null, string? customerToken = null)
    {
        var principal = await Authenticate.HandleAsync(new AuthenticatePublishableKeyQuery(key, origin, deviceToken, customerToken));
        CustomerSessionId = principal?.CustomerSessionId is { } session ? global::Storefront.Domain.CustomerSessionId.From(session) : null;
        CustomerId = principal?.CustomerId is { } customer ? SaasRestaurant.BuildingBlocks.Domain.CustomerId.From(customer) : null;
        CurrentTenantId = principal is null ? null : TenantId.From(principal.TenantId);
        AppId = principal is null ? null : SalesAppId.From(principal.AppId);
        DeviceId = principal?.DeviceId is { } device ? KioskDeviceId.From(device) : null;
        DeviceLocationId = principal?.DeviceLocationId is { } location ? LocationId.From(location) : null;
        return principal;
    }

    // ---- Ports

    Task<StorefrontRestaurant?> IStorefrontTenants.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Restaurants.GetValueOrDefault(tenantId));

    Task<SalesApp?> ISalesAppRepository.FindAsync(TenantId tenantId, SalesAppId id, CancellationToken cancellationToken) =>
        Task.FromResult(Apps.SingleOrDefault(app => app.TenantId == tenantId && app.Id == id));

    Task<IReadOnlyList<SalesApp>> ISalesAppRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SalesApp>>(Apps.Where(app => app.TenantId == tenantId).ToList());

    void ISalesAppRepository.Add(SalesApp app) => Apps.Add(app);

    Task<SalesApp?> ISalesAppKeyLookup.FindByKeyHashAsync(PublishableKeyHash keyHash, CancellationToken cancellationToken) =>
        Task.FromResult(Apps.SingleOrDefault(app => app.KeyHash == keyHash));

    Task<OnlineOrderingSettings?> IOnlineOrderingSettingsRepository.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(Settings.SingleOrDefault(settings => settings.TenantId == tenantId && settings.LocationId == locationId));

    Task<IReadOnlyList<OnlineOrderingSettings>> IOnlineOrderingSettingsRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OnlineOrderingSettings>>(Settings.Where(settings => settings.TenantId == tenantId).ToList());

    void IOnlineOrderingSettingsRepository.Add(OnlineOrderingSettings settings) => Settings.Add(settings);

    Task<IReadOnlyList<StorefrontLocation>> IStorefrontLocations.ListAsync(TenantId tenantId, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StorefrontLocation>>(Locations
            .Where(item => item.Tenant == tenantId)
            .Select(item => OpenAt is null ? item.Location : item.Location with { IsOpenNow = OpenAt(item.Location.LocationId, atUtc) })
            .ToList());

    /// <summary>Ouverture à un instant ; null = celle donnée à la création de l'établissement, quel que soit l'instant.</summary>
    public Func<LocationId, DateTimeOffset, bool>? OpenAt { get; set; }

    Task<long?> IStorefrontEntitlements.LimitAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken) =>
        Task.FromResult(Options.Contains((tenantId, channel)) ? KioskLimit : null);

    Task<bool> IStorefrontTickets.PrintAsync(TenantId tenantId, LocationId locationId, string reference, KioskTicket ticket, CancellationToken cancellationToken)
    {
        if (!HasTicketPrinter)
        {
            return Task.FromResult(false);
        }

        Tickets.Add((locationId, reference, ticket));
        return Task.FromResult(true);
    }

    Task<KioskDevice?> IKioskDeviceRepository.FindAsync(TenantId tenantId, KioskDeviceId id, CancellationToken cancellationToken) =>
        Task.FromResult(Kiosks.SingleOrDefault(device => device.TenantId == tenantId && device.Id == id));

    Task<IReadOnlyList<KioskDevice>> IKioskDeviceRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KioskDevice>>(Kiosks.Where(device => device.TenantId == tenantId).ToList());

    Task<int> IKioskDeviceRepository.CountActiveAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Kiosks.Count(device => device.TenantId == tenantId && device.IsActive));

    void IKioskDeviceRepository.Add(KioskDevice device) => Kiosks.Add(device);

    Task<KioskDevice?> IKioskDeviceLookup.FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Kiosks.SingleOrDefault(device => device.TokenHash == tokenHash));

    Task<KioskPairing?> IKioskPairingRepository.FindAsync(TenantId tenantId, KioskPairingId id, CancellationToken cancellationToken) =>
        Task.FromResult(Pairings.SingleOrDefault(pairing => pairing.TenantId == tenantId && pairing.Id == id));

    Task<KioskPairing?> IKioskPairingRepository.FindPendingByCodeAsync(TenantId tenantId, PublishableKeyHash codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Pairings.FirstOrDefault(pairing => pairing.TenantId == tenantId && pairing.CodeHash == codeHash
            && pairing.Status == KioskPairingStatus.Pending && !pairing.IsExpired(nowUtc)));

    Task<int> IKioskPairingRepository.CountApprovedAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Pairings.Count(pairing => pairing.TenantId == tenantId && pairing.Status == KioskPairingStatus.Approved && !pairing.IsExpired(nowUtc)));

    void IKioskPairingRepository.Add(KioskPairing pairing) => Pairings.Add(pairing);

    Task<bool> IStorefrontEntitlements.AllowsAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken) =>
        Task.FromResult(Options.Contains((tenantId, channel)));

    Task<StorefrontCatalogContent> IStorefrontCatalog.GetAsync(TenantId tenantId, LocationId locationId, SalesAppKind channel, CancellationToken cancellationToken) =>
        Task.FromResult(Catalogs.GetValueOrDefault((tenantId, locationId, channel)) ?? new StorefrontCatalogContent([], [], []));

    Task<CartQuote> IStorefrontOrders.QuoteAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, SalesAppKind channel, IReadOnlyList<CartLine> lines, CartAdjustments? adjustments,
        CancellationToken cancellationToken)
    {
        var quoted = lines.Select(line => Prices.TryGetValue(line.ProductId, out var price)
                ? new CartQuoteLine(line.ProductId, line.VariantId, "Produit", null, line.Quantity, price, [], line.Note, price * line.Quantity)
                : throw new DomainException(new DomainError("catalog.product.not_sellable", "Not sellable.")))
            .ToList();
        var subTotal = quoted.Sum(line => line.LineTotal);
        var discount = Math.Min((adjustments?.Discount?.Amount ?? 0m) + (adjustments?.Reward?.Amount ?? 0m), subTotal);
        var fee = adjustments?.DeliveryFee;
        return Task.FromResult(new CartQuote(
            Restaurants[tenantId].CurrencyCode, true, quoted, subTotal, discount, 0m, subTotal - discount + (fee ?? 0m), fee));
    }

    Task<StorefrontDeliveryOffer?> IStorefrontDelivery.OfferAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(CurrentTenantId == tenantId ? DeliveryOffers.GetValueOrDefault(locationId) : null);

    Task<StorefrontDeliveryTracking?> IStorefrontDelivery.TrackAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.TryGetValue(orderId, out var delivery) && Orders[orderId].Tenant == tenantId
            ? DeliveryOffers[Orders[orderId].Order.LocationId].Zones.Single(zone => zone.ZoneId == delivery.ZoneId) is var zone
                ? new StorefrontDeliveryTracking(zone.Name, zone.Fee, "Pending", Clock.UtcNow.AddMinutes(zone.EstimatedMinutes))
                : null
            : null);

    Task<CouponPreview> IStorefrontCoupons.PreviewAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, string currencyCode, IReadOnlyList<CartQuoteLine> lines, string code,
        CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (CurrentTenantId != tenantId || !Coupons.TryGetValue(normalized, out var coupon))
        {
            return Task.FromResult(new CouponPreview(normalized, false, "promotions.coupon.not_found", null, null, 0m));
        }

        return Task.FromResult(coupon.Refusal is { } refusal
            ? new CouponPreview(normalized, false, refusal, null, null, 0m)
            : new CouponPreview(normalized, true, null, coupon.PromotionId, "Promo " + normalized, Math.Min(coupon.Amount, lines.Sum(line => line.LineTotal))));
    }

    async Task<PlacedOrder> IStorefrontOrders.PlaceAsync(TenantId tenantId, OrderToPlace order, CancellationToken cancellationToken)
    {
        if (OrdersRefusePlacing)
        {
            throw new DomainException(PlacingRefused);
        }

        var lines = order.Lines.Sum(line => Prices[line.ProductId] * line.Quantity);
        CartDiscount? discount = null;
        if (order.CouponCode is { } code)
        {
            var coupon = Coupons[code];
            DomainException.ThrowIf(coupon.Refusal is not null, new DomainError(coupon.Refusal ?? "x", "Refused."));
            discount = new CartDiscount(coupon.PromotionId, Math.Min(coupon.Amount, lines), code);
        }

        CartReward? reward = null;
        if (order.RewardId is { } rewardId)
        {
            var chosen = Rewards.Single(item => item.Id == rewardId);
            Balances[order.CustomerId!.Value] -= chosen.Cost;
            reward = new CartReward(rewardId, chosen.Discount, chosen.Name);
        }

        var fee = order.Delivery is { } delivery
            ? DeliveryOffers[order.LocationId].Zones.Single(zone => zone.ZoneId == delivery.ZoneId).Fee
            : (decimal?)null;
        var quote = await ((IStorefrontOrders)this).QuoteAsync(
            tenantId, order.LocationId, order.Mode, order.Channel, order.Lines, new CartAdjustments(discount, fee, reward), cancellationToken);
        var id = OrderId.New();
        Orders[id] = (tenantId, order, GuestOrderStatus.Received, quote.Total, null);
        OrderAdjustments[id] = (quote.DiscountTotal, quote.DeliveryFee);
        if (order.CouponCode is { } redeemed)
        {
            RedeemedCoupons.Add((redeemed, id));
        }

        if (order.Delivery is { } placedDelivery)
        {
            Deliveries[id] = placedDelivery;
        }

        return new PlacedOrder(id, $"20260115-{Orders.Count:D3}", quote.CurrencyCode, quote.Total);
    }

    Task IStorefrontOrders.AcceptAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken)
    {
        Orders[orderId] = Orders[orderId] with { Status = GuestOrderStatus.Accepted };
        return Task.CompletedTask;
    }

    Task IStorefrontOrders.CancelAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken)
    {
        Orders[orderId] = Orders[orderId] with { Status = GuestOrderStatus.Cancelled, CancelReason = reason };
        return Task.CompletedTask;
    }

    Task<StorefrontOrderView?> IStorefrontOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId
            ? new StorefrontOrderView(
                orderId, order.Order.LocationId, order.Order.Mode, "20260115-001", order.Status, Restaurants[tenantId].CurrencyCode, [], order.Total, Clock.UtcNow,
                order.Order.ScheduledForUtc, OrderAdjustments.GetValueOrDefault(orderId).Discount, OrderAdjustments.GetValueOrDefault(orderId).Fee)
            : null);

    Task<IReadOnlyList<OnlinePaymentOption>> IStorefrontPayments.OptionsAsync(
        TenantId tenantId, LocationId locationId, string currencyCode, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OnlinePaymentOption>>(PaymentOptions.GetValueOrDefault(tenantId) ?? []);

    Task<StartedPayment> IStorefrontPayments.StartAsync(TenantId tenantId, OrderId orderId, OnlinePaymentRequest request, CancellationToken cancellationToken)
    {
        for (var i = 0; i < Payments.Count; i++)
        {
            if (Payments[i].Order == orderId && Payments[i].Status == "Pending")
            {
                Payments[i] = Payments[i] with { Status = "Cancelled" };
            }
        }

        var id = Guid.NewGuid();
        Payments.Add((id, orderId, "Pending", Orders[orderId].Total, request.ReturnUrl));
        return Task.FromResult(new StartedPayment(id, new Uri($"https://pay.test/{request.Provider}/{id}")));
    }

    Task<OrderPaymentSummary> IStorefrontPayments.SummaryAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken)
    {
        var mine = Payments.Where(payment => payment.Order == orderId).ToList();
        var paid = mine.Where(payment => payment.Status == "Captured").Sum(payment => payment.Amount);
        return Task.FromResult(new OrderPaymentSummary(
            paid, Orders[orderId].Total - paid, mine.Any(payment => payment.Status == "Pending"), mine.Any(payment => payment.Status == "Refunded")));
    }

    Task IStorefrontPayments.CancelPendingAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken)
    {
        for (var i = 0; i < Payments.Count; i++)
        {
            if (Payments[i].Order == orderId && Payments[i].Status == "Pending")
            {
                Payments[i] = Payments[i] with { Status = "Cancelled" };
            }
        }

        return Task.CompletedTask;
    }

    Task IStorefrontPayments.RefundAllAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken)
    {
        for (var i = 0; i < Payments.Count; i++)
        {
            if (Payments[i].Order == orderId && Payments[i].Status == "Captured")
            {
                Payments[i] = Payments[i] with { Status = "Refunded" };
            }
        }

        return Task.CompletedTask;
    }

    // Même règle que l'hôte : CustomerContactMatch sur les fiches qui portent le contact exact.
    private Guid? Match(TenantId tenantId, string? email, string? phone)
    {
        List<global::Storefront.Application.Customers.CustomerContactCandidate> By(Func<(TenantId Tenant, Guid Id, string? Email, string? Phone), bool> filter) =>
            Customers.Where(customer => customer.Tenant == tenantId && Profiles.ContainsKey(customer.Id) && filter(customer))
                .Select(customer => new global::Storefront.Application.Customers.CustomerContactCandidate(customer.Id, customer.Email is not null, customer.Phone is not null))
                .ToList();
        return global::Storefront.Application.Customers.CustomerContactMatch.Pick(
            email is null ? [] : By(customer => customer.Email == email), phone is null ? [] : By(customer => customer.Phone == phone));
    }

    Task<Guid> IStorefrontCustomers.FindOrCreateAsync(TenantId tenantId, GuestCustomer customer, CancellationToken cancellationToken)
    {
        CustomerRequests.Add(customer);
        var email = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email.Trim();
        var phone = string.IsNullOrWhiteSpace(customer.Phone) ? null : customer.Phone.Trim();
        if (Match(tenantId, email, phone) is { } existing)
        {
            return Task.FromResult(existing);
        }

        var id = Guid.NewGuid();
        Customers.Add((tenantId, id, email, phone));
        Profiles[id] = new CustomerProfile(SaasRestaurant.BuildingBlocks.Domain.CustomerId.From(id), customer.Name ?? string.Empty, email, phone);
        if (customer.MarketingConsent)
        {
            ConsentsAtCreation.Add(id);
        }

        return Task.FromResult(id);
    }

    Task<CustomerId?> IStorefrontCustomers.FindByContactAsync(TenantId tenantId, string? email, string? phone, CancellationToken cancellationToken) =>
        Task.FromResult(Match(tenantId, email?.Trim(), phone?.Trim()) is { } id ? SaasRestaurant.BuildingBlocks.Domain.CustomerId.From(id) : (CustomerId?)null);

    Task<CustomerProfile?> IStorefrontCustomers.ProfileAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.Any(entry => entry.Tenant == tenantId && entry.Id == customerId.Value) ? Profiles.GetValueOrDefault(customerId.Value) : null);

    Task IStorefrontCustomers.GrantMarketingConsentAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken)
    {
        ConsentsGranted.Add(customerId.Value);
        return Task.CompletedTask;
    }

    Task<OtpChallengeIssued> IStorefrontOtp.RequestAsync(
        TenantId tenantId, CustomerSignInChannel channel, string address, string? language, CancellationToken cancellationToken)
    {
        var challenge = Guid.NewGuid();
        SentCodes.Add((challenge, channel, address, false));
        return Task.FromResult(new OtpChallengeIssued(challenge, Clock.UtcNow.AddMinutes(10)));
    }

    Task IStorefrontOtp.VerifyAsync(TenantId tenantId, Guid challengeId, string code, CancellationToken cancellationToken)
    {
        var index = SentCodes.FindIndex(sent => sent.Challenge == challengeId);
        if (index < 0 || SentCodes[index].Used || code != GoodCode)
        {
            throw new DomainException(new DomainError(index >= 0 && SentCodes[index].Used ? "notifications.otp.already_used" : "notifications.otp.invalid", "Refused."));
        }

        SentCodes[index] = SentCodes[index] with { Used = true };
        return Task.CompletedTask;
    }

    Task<CustomerLoyalty> IStorefrontLoyalty.SummaryAsync(TenantId tenantId, CustomerId customerId, string currencyCode, CancellationToken cancellationToken)
    {
        var balance = Balances.GetValueOrDefault(customerId.Value);
        return Task.FromResult(LoyaltyEnabled
            ? new CustomerLoyalty(true, balance, 1m, Rewards.Select(reward => new LoyaltyRewardOption(reward.Id, reward.Name, reward.Cost, reward.Discount, balance >= reward.Cost)).ToList())
            : new CustomerLoyalty(false, 0, 0m, []));
    }

    Task<CustomerSession?> ICustomerSessionRepository.FindAsync(TenantId tenantId, CustomerSessionId id, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.SingleOrDefault(session => session.TenantId == tenantId && session.Id == id));

    void ICustomerSessionRepository.Add(CustomerSession session) => Sessions.Add(session);

    Task<CustomerSession?> ICustomerSessionLookup.FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.SingleOrDefault(session => session.TokenHash == tokenHash));

    Task<IReadOnlyList<StorefrontOrderSummary>> IStorefrontOrders.ListForCustomerAsync(TenantId tenantId, CustomerId customerId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StorefrontOrderSummary>>(Orders
            .Where(entry => entry.Value.Tenant == tenantId && entry.Value.Order.CustomerId == customerId.Value)
            .Select(entry => new StorefrontOrderSummary(
                entry.Key, "20260115-001", entry.Value.Order.LocationId, entry.Value.Order.Mode, entry.Value.Status, Restaurants[tenantId].CurrencyCode, entry.Value.Total, Clock.UtcNow))
            .Take(take)
            .ToList());

    Task<OnlineOrder?> IOnlineOrderRepository.FindAsync(TenantId tenantId, OnlineOrderId id, CancellationToken cancellationToken) =>
        Task.FromResult(OnlineOrders.SingleOrDefault(order => order.TenantId == tenantId && order.Id == id));

    Task<OnlineOrder?> IOnlineOrderRepository.FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(OnlineOrders.SingleOrDefault(order => order.TenantId == tenantId && order.OrderId == orderId));

    Task<IReadOnlyList<OnlineOrder>> IOnlineOrderRepository.ListWaitingAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OnlineOrder>>(OnlineOrders
            .Where(order => order.TenantId == tenantId && order.LocationId == locationId && order.NextDeadlineUtc is not null)
            .OrderBy(order => order.NextDeadlineUtc)
            .ToList());

    public ListWaitingOnlineOrdersHandler Waiting => new(Workspace, this);

    void IOnlineOrderRepository.Add(OnlineOrder order) => OnlineOrders.Add(order);

    Task<int> IOnlineOrderRepository.CountAwaitingPaymentAsync(
        TenantId tenantId, SalesAppId appId, string? clientKey, CustomerId? customerId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(clientKey is null && customerId is null ? 0 : OnlineOrders.Count(order =>
            order.TenantId == tenantId && order.AppId == appId && order.IsAwaitingPayment && order.NextDeadlineUtc > nowUtc
            && ((clientKey != null && order.ClientKey == clientKey) || (customerId != null && order.CustomerId == customerId))));

    Task<int> IOnlineOrderRepository.CountInSlotAsync(TenantId tenantId, LocationId locationId, DateTimeOffset slotStartUtc, CancellationToken cancellationToken) =>
        Task.FromResult(OnlineOrders.Count(order => order.TenantId == tenantId && order.LocationId == locationId && order.SlotStartUtc == slotStartUtc && order.HoldsSlot));

    Task<IReadOnlyDictionary<DateTimeOffset, int>> IOnlineOrderRepository.CountBySlotAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<DateTimeOffset, int>>(OnlineOrders
            .Where(order => order.TenantId == tenantId && order.LocationId == locationId && order.HoldsSlot
                && order.SlotStartUtc is { } slot && slot >= fromUtc && slot < toUtc)
            .GroupBy(order => order.SlotStartUtc!.Value)
            .ToDictionary(group => group.Key, group => group.Count()));

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

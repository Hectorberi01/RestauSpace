using Storefront.Domain;

namespace Storefront.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (console ou clé publique), jamais
// d'une valeur de requête. Une ressource d'un autre tenant n'est tout simplement pas trouvée.

/// <summary>
/// Unit of work du module Storefront : une transaction sur le schéma du module, domain events écrits dans son outbox.
/// </summary>
public interface IStorefrontUnitOfWork : IUnitOfWork;

public interface ISalesAppRepository
{
    Task<SalesApp?> FindAsync(TenantId tenantId, SalesAppId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<SalesApp>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(SalesApp app);
}

/// <summary>
/// Retrouve une application par l'empreinte de sa clé, TOUS tenants confondus : c'est ainsi qu'une requête publique
/// apprend son tenant. Lecture seule.
/// </summary>
public interface ISalesAppKeyLookup
{
    Task<SalesApp?> FindByKeyHashAsync(PublishableKeyHash keyHash, CancellationToken cancellationToken = default);
}

public interface IOnlineOrderingSettingsRepository
{
    Task<OnlineOrderingSettings?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<OnlineOrderingSettings>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(OnlineOrderingSettings settings);
}

/// <summary>Un créneau d'ouverture hebdomadaire, en heure locale (« HH:mm »).</summary>
public sealed record StorefrontOpeningSlot(DayOfWeek Day, string Start, string End, bool EndsNextDay);

/// <summary>Un établissement (non archivé) du tenant et son ouverture à un instant, fourni par l'hôte depuis Locations.</summary>
public sealed record StorefrontLocation(
    LocationId LocationId,
    string Name,
    string Street,
    string City,
    string TimeZoneId,
    bool IsOperational,
    bool IsOpenNow,
    DateTimeOffset? NextOpeningUtc,
    IReadOnlyList<StorefrontOpeningSlot> WeeklyHours,
    string? TemporaryClosureReason,
    StorefrontPlace? Place = null,
    IReadOnlyList<StorefrontSpecialDay>? SpecialDays = null);

/// <summary>Où trouver et joindre l'établissement : code postal, pays (ISO 3166-1 alpha-2), position, coordonnées publiques.</summary>
public sealed record StorefrontPlace(
    string? PostalCode, string CountryCode, decimal? Latitude, decimal? Longitude, string? Phone, string? WhatsApp, string? Email)
{
    public override string ToString() => "StorefrontPlace(***)";
}

/// <summary>Un jour aux horaires exceptionnels (heure locale) : fermé, ou ouvert sur ces seules plages.</summary>
public sealed record StorefrontSpecialDay(DateOnly Date, bool IsClosed, IReadOnlyList<StorefrontTimeRange> Ranges, string? Reason);

public sealed record StorefrontTimeRange(string Start, string End, bool EndsNextDay);

public interface IStorefrontLocations
{
    Task<IReadOnlyList<StorefrontLocation>> ListAsync(TenantId tenantId, DateTimeOffset atUtc, CancellationToken cancellationToken = default);
}

/// <summary>Ce que la vente en ligne doit savoir du restaurant, fourni par l'hôte depuis Tenants.</summary>
public sealed record StorefrontRestaurant(
    string Name,
    string? LogoUrl,
    string? PrimaryColor,
    string CurrencyCode,
    bool PricesIncludeTax,
    bool AcceptsBusinessWrites,
    StorefrontProfile? Profile = null);

/// <summary>Présentation publique du restaurant. <see cref="StorefrontLink.Network"/> : Website, Facebook, Instagram, TikTok, X, YouTube, GoogleMaps.</summary>
public sealed record StorefrontProfile(string? Tagline, string? Description, string? CoverImageUrl, IReadOnlyList<StorefrontLink> Links);

public sealed record StorefrontLink(string Network, string Url);

public interface IStorefrontTenants
{
    /// <summary>Null pour un restaurant inconnu.</summary>
    Task<StorefrontRestaurant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les options souscrites, fournies par l'hôte depuis Billing : le restaurant peut-il vendre par ce canal (site,
/// application, borne) ? Sans abonnement vivant ou sans l'option : non.
/// </summary>
public interface IStorefrontEntitlements
{
    Task<bool> AllowsAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken = default);

    /// <summary>La limite du droit (nombre de bornes pour le canal borne) ; null = sans limite ou canal fermé.</summary>
    Task<long?> LimitAsync(TenantId tenantId, SalesAppKind channel, CancellationToken cancellationToken = default);
}

/// <summary>L'application de vente qui fait la requête (clé publique authentifiée) ; null hors d'une requête publique.</summary>
public interface IStorefrontClient
{
    SalesAppId? AppId { get; }

    /// <summary>La borne appairée qui fait la requête (jeton d'appareil vérifié) ; null pour un site, une application, ou une borne non appairée.</summary>
    KioskDeviceId? DeviceId { get; }

    /// <summary>L'établissement de cette borne : elle ne voit et ne commande que lui.</summary>
    LocationId? DeviceLocationId { get; }

    /// <summary>La session du client connecté (jeton de session vérifié, actif, de cette application) ; null pour un invité.</summary>
    CustomerSessionId? CustomerSessionId { get; }

    /// <summary>Le client de cette session.</summary>
    CustomerId? CustomerId { get; }
}

/// <summary>
/// Le catalogue commandable d'un établissement du tenant pour un canal, fourni par l'hôte depuis Catalog (menus publiés,
/// produits visibles sur ce canal, groupes d'options) et Files (photos publiques). Fail-closed : un tenant autre que
/// celui du contexte n'a aucun catalogue.
/// </summary>
public interface IStorefrontCatalog
{
    Task<Queries.StorefrontCatalogContent> GetAsync(TenantId tenantId, LocationId locationId, SalesAppKind channel, CancellationToken cancellationToken = default);
}

// ---- Commande invité (phase 3)

/// <summary>Pour une formule : le produit choisi à une étape, avec sa variante et ses options.</summary>
public sealed record CartComponent(Guid StepId, Guid ProductId, Guid? VariantId = null, IReadOnlyList<Guid>? ModifierIds = null);

/// <summary>
/// Une ligne de panier envoyée par l'application : produit, variante, options choisies, quantité, note cuisine ; pour
/// une formule, les produits choisis à ses étapes (<paramref name="Components"/>).
/// </summary>
public sealed record CartLine(
    Guid ProductId, int Quantity, Guid? VariantId = null, IReadOnlyList<Guid>? ModifierIds = null, string? Note = null,
    IReadOnlyList<CartComponent>? Components = null);

public sealed record CartQuoteModifier(Guid Id, string Name, decimal PriceDelta);

/// <summary>Un produit choisi dans une formule : étape, nom, supplément unitaire et options.</summary>
public sealed record CartQuoteComponent(
    string StepName, Guid ProductId, string Name, string? VariantName, decimal Supplement, IReadOnlyList<CartQuoteModifier> Modifiers);

/// <param name="UnitPrice">Prix unitaire de la variante (ou du produit), sans les options.</param>
/// <param name="LineTotal">(prix unitaire + options) x quantité.</param>
/// <param name="Components">Pour une formule : les produits choisis, leurs suppléments et options (compris dans LineTotal).</param>
public sealed record CartQuoteLine(
    Guid ProductId, Guid? VariantId, string Name, string? VariantName, int Quantity, decimal UnitPrice, IReadOnlyList<CartQuoteModifier> Modifiers,
    string? Note, decimal LineTotal, IReadOnlyList<CartQuoteComponent>? Components = null);

/// <summary>Les montants d'un panier, calculés par Orders avec les règles d'une vraie commande.</summary>
/// <param name="Total">À payer, frais de livraison compris.</param>
/// <param name="DeliveryFee">Frais de livraison compris dans le total ; null sans livraison.</param>
public sealed record CartQuote(
    string CurrencyCode, bool PricesIncludeTax, IReadOnlyList<CartQuoteLine> Lines, decimal SubTotal, decimal DiscountTotal, decimal TaxTotal, decimal Total,
    decimal? DeliveryFee = null);

/// <summary>La remise d'un coupon, telle que Promotions l'a calculée sur le panier.</summary>
public sealed record CartDiscount(Guid PromotionId, decimal Amount, string Label);

/// <summary>La remise d'une récompense de fidélité, au prix de la récompense.</summary>
public sealed record CartReward(Guid RewardId, decimal Amount, string Label);

/// <summary>Ce qui s'ajoute aux lignes dans le devis : la remise du coupon, celle d'une récompense et les frais de livraison de la zone.</summary>
public sealed record CartAdjustments(CartDiscount? Discount, decimal? DeliveryFee, CartReward? Reward = null);

/// <summary>
/// La livraison demandée au checkout : zone, destinataire et adresse (données personnelles : jamais journalisées, jamais
/// dans un événement). <paramref name="DeliverAtUtc"/> : heure demandée, sinon dès que possible.
/// </summary>
public sealed record DeliveryToPlace(
    Guid ZoneId, string CustomerName, string CustomerPhone, string AddressLine, string? Landmark, string? Instructions, DateTimeOffset? DeliverAtUtc)
{
    public override string ToString() => $"DeliveryToPlace({ZoneId}, ***)";
}

/// <summary>Ce que la vente en ligne demande à Orders de créer, puis de valider (Placed).</summary>
public sealed record OrderToPlace(
    LocationId LocationId,
    ServiceMode Mode,
    IReadOnlyList<CartLine> Lines,
    Guid? CustomerId,
    SalesAppKind Channel,
    SalesAppId AppId,
    DateTimeOffset? ScheduledForUtc,
    string? CouponCode = null,
    DeliveryToPlace? Delivery = null,
    Guid? RewardId = null,
    TableSessionId? TableSessionId = null)
{
    public override string ToString() => $"OrderToPlace({LocationId}, {Mode}, {Lines.Count} lignes)";
}

/// <summary>Une table du tenant courant, trouvée par le jeton de son QR code.</summary>
public sealed record StorefrontTable(Guid TableId, LocationId LocationId, string Label, string AreaName, bool IsInService);

/// <summary>
/// Les tables (module Floor, via l'hôte) pour la commande à table par QR code : la table d'un jeton, et la session de
/// table où rattacher la commande (celle en cours, sinon une nouvelle, ouverte pour ces convives).
/// </summary>
public interface IStorefrontTables
{
    Task<StorefrontTable?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default);

    Task<TableSessionId> SeatAsync(TenantId tenantId, Guid tableId, CancellationToken cancellationToken = default);
}

public sealed record PlacedOrder(OrderId OrderId, string Number, string CurrencyCode, decimal Total);

/// <summary>L'état d'une commande tel qu'un client le voit.</summary>
public enum GuestOrderStatus
{
    /// <summary>Reçue, pas encore acceptée par le restaurant.</summary>
    Received = 1,
    Accepted = 2,
    Preparing = 3,
    Ready = 4,
    Completed = 5,
    Cancelled = 6,
}

/// <summary>Une commande lue dans Orders, sans donnée personnelle ni motif saisi par le personnel.</summary>
public sealed record StorefrontOrderView(
    OrderId OrderId,
    LocationId LocationId,
    ServiceMode Mode,
    string Number,
    GuestOrderStatus Status,
    string CurrencyCode,
    IReadOnlyList<CartQuoteLine> Lines,
    decimal Total,
    DateTimeOffset? PlacedAtUtc,
    DateTimeOffset? ScheduledForUtc,
    decimal DiscountTotal = 0m,
    decimal? DeliveryFee = null);

/// <summary>
/// Les commandes, fournies par l'hôte depuis Orders, pour le tenant du contexte. Fail-closed : un tenant autre que celui
/// du contexte n'a aucune commande et ne peut rien créer.
/// </summary>
public interface IStorefrontOrders
{
    /// <summary>
    /// Devis du panier (rien n'est enregistré), avec la remise et les frais donnés appliqués par la commande elle-même. Un
    /// produit invendable ou une option invalide lève l'erreur d'Orders ou de Catalog. Les prix sont ceux de
    /// l'établissement et du canal (<paramref name="channel"/>), comme à la validation.
    /// </summary>
    Task<CartQuote> QuoteAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, SalesAppKind channel, IReadOnlyList<CartLine> lines, CartAdjustments? adjustments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée la commande, ajoute les lignes, applique le coupon (compté par Promotions), crée la fiche de livraison et pose
    /// ses frais, puis la valide ; en cas d'échec, le brouillon est annulé (ce qui rend le coupon et annule la fiche).
    /// </summary>
    Task<PlacedOrder> PlaceAsync(TenantId tenantId, OrderToPlace order, CancellationToken cancellationToken = default);

    Task AcceptAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default);

    Task<StorefrontOrderView?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Les commandes validées du client, de la plus récente à la plus ancienne (brouillons exclus).</summary>
    Task<IReadOnlyList<StorefrontOrderSummary>> ListForCustomerAsync(
        TenantId tenantId, CustomerId customerId, int take, CancellationToken cancellationToken = default);
}

/// <summary>Une commande du client, pour son historique : sans les lignes.</summary>
public sealed record StorefrontOrderSummary(
    OrderId OrderId, string Number, LocationId LocationId, ServiceMode Mode, GuestOrderStatus Status, string CurrencyCode, decimal Total, DateTimeOffset CreatedAtUtc);

/// <summary>Le client invité tel qu'il se présente au checkout.</summary>
/// <param name="Language">Facultatif : la langue du client sur le site (<c>fr</c>, <c>en</c>), pour ses messages ; posée à la création de sa fiche seulement.</param>
public sealed record GuestCustomer(string? Name, string? Email, string? Phone, bool MarketingConsent, string? Language = null)
{
    public override string ToString() => "GuestCustomer (***)";
}

/// <summary>
/// Les clients, fournis par l'hôte depuis Customers : la fiche retrouvée par e-mail ou téléphone dans le restaurant, ou
/// créée selon sa règle de contact ; le consentement marketing est enregistré s'il est donné. Fail-closed sur le tenant.
/// </summary>
public interface IStorefrontCustomers
{
    Task<Guid> FindOrCreateAsync(TenantId tenantId, GuestCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>La fiche active qui porte cet e-mail ou ce téléphone (normalisés par Customers) ; null sinon.</summary>
    Task<CustomerId?> FindByContactAsync(TenantId tenantId, string? email, string? phone, CancellationToken cancellationToken = default);

    /// <summary>Nom et coordonnées d'une fiche active ; null si elle n'existe pas ou est anonymisée.</summary>
    Task<CustomerProfile?> ProfileAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    /// <summary>Le client accepte les offres du restaurant, pour chacun de ses moyens de contact.</summary>
    Task GrantMarketingConsentAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>Le client connecté, vu par lui-même.</summary>
public sealed record CustomerProfile(CustomerId CustomerId, string Name, string? Email, string? Phone)
{
    public override string ToString() => $"CustomerProfile({CustomerId}, ***)";
}

public interface IOnlineOrderRepository
{
    /// <summary>Les commandes de l'établissement qui occupent encore ce créneau (capacité par créneau).</summary>
    Task<int> CountInSlotAsync(TenantId tenantId, LocationId locationId, DateTimeOffset slotStartUtc, CancellationToken cancellationToken = default);

    /// <summary>Le nombre de commandes par créneau occupé, de <paramref name="fromUtc"/> (inclus) à <paramref name="toUtc"/> (exclu).</summary>
    Task<IReadOnlyDictionary<DateTimeOffset, int>> CountBySlotAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task<OnlineOrder?> FindAsync(TenantId tenantId, OnlineOrderId id, CancellationToken cancellationToken = default);

    Task<OnlineOrder?> FindByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les commandes de l'établissement qui attendent encore leur paiement ou leur acceptation.</summary>
    Task<IReadOnlyList<OnlineOrder>> ListWaitingAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    void Add(OnlineOrder order);
}

/// <summary>Une commande en attente d'acceptation dont l'échéance est passée : (tenant, id).</summary>
public sealed record OverdueOnlineOrder(TenantId TenantId, OnlineOrderId Id);

/// <summary>Pour le job SEULEMENT : tous les tenants.</summary>
public interface IOverdueOnlineOrders
{
    Task<IReadOnlyList<OverdueOnlineOrder>> ListAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);
}

// ---- Paiement en ligne (phase 4)

/// <summary>Un prestataire de paiement en ligne que le restaurant accepte dans cet établissement.</summary>
/// <param name="Methods">Moyens proposés (carte, mobile money…), tels que Payments les nomme.</param>
public sealed record OnlinePaymentOption(string Provider, string DisplayName, IReadOnlyList<string> Methods);

/// <param name="ReturnUrl">Page de retour déjà vérifiée contre l'application ; null = celle de la plateforme.</param>
public sealed record OnlinePaymentRequest(string Provider, string? Method, string? PayerPhone, Uri? ReturnUrl)
{
    public override string ToString() => $"OnlinePaymentRequest ({Provider})";
}

/// <param name="CheckoutUrl">Page du prestataire où envoyer le client ; null quand la demande est poussée sur son téléphone.</param>
public sealed record StartedPayment(Guid PaymentId, Uri? CheckoutUrl);

/// <param name="Paid">Montant encaissé (net des remboursements).</param>
/// <param name="Due">Reste à payer.</param>
/// <param name="HasPending">Un paiement en ligne attend la réponse du prestataire.</param>
public sealed record OrderPaymentSummary(decimal Paid, decimal Due, bool HasPending, bool HasRefund);

/// <summary>
/// Les paiements, fournis par l'hôte depuis Payments, pour le tenant du contexte (fail-closed sinon). Démarrer un
/// paiement reprend un paiement échoué, ou remplace un paiement resté en attente.
/// </summary>
public interface IStorefrontPayments
{
    /// <summary>Les moyens en ligne proposés pour ce montant dans cet établissement (bornes et pays compris).</summary>
    Task<IReadOnlyList<OnlinePaymentOption>> OptionsAsync(
        TenantId tenantId, LocationId locationId, string currencyCode, decimal amount, CancellationToken cancellationToken = default);

    Task<StartedPayment> StartAsync(TenantId tenantId, OrderId orderId, OnlinePaymentRequest request, CancellationToken cancellationToken = default);

    Task<OrderPaymentSummary> SummaryAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Annule les paiements en ligne encore en attente de la commande.</summary>
    Task CancelPendingAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Rembourse tout ce qui a été encaissé sur la commande.</summary>
    Task RefundAllAsync(TenantId tenantId, OrderId orderId, string reason, CancellationToken cancellationToken = default);
}

// ---- Bornes et ticket client (phase 6)

public interface IKioskDeviceRepository
{
    Task<KioskDevice?> FindAsync(TenantId tenantId, KioskDeviceId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par date d'appairage.</summary>
    Task<IReadOnlyList<KioskDevice>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(KioskDevice device);
}

/// <summary>Retrouve une borne par l'empreinte de son jeton, TOUS tenants confondus (authentification). Lecture seule.</summary>
public interface IKioskDeviceLookup
{
    Task<KioskDevice?> FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken = default);
}

public interface IKioskPairingRepository
{
    Task<KioskPairing?> FindAsync(TenantId tenantId, KioskPairingId id, CancellationToken cancellationToken = default);

    /// <summary>L'appairage en attente, non expiré, dont le code a cette empreinte.</summary>
    Task<KioskPairing?> FindPendingByCodeAsync(TenantId tenantId, PublishableKeyHash codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Appairages approuvés, non expirés, dont la borne n'a pas encore retiré son jeton (ils comptent dans la limite).</summary>
    Task<int> CountApprovedAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    void Add(KioskPairing pairing);
}

public sealed record KioskTicketLine(int Quantity, string Name, string? Details, string Amount);

/// <summary>Le ticket remis au client d'une borne : textes déjà formatés, aucune donnée personnelle.</summary>
public sealed record KioskTicket(
    string RestaurantName, string LocationName, string OrderNumber, string ServiceMode, IReadOnlyList<KioskTicketLine> Lines, string Total, string PaymentText,
    string PrintedAt);

/// <summary>L'impression, fournie par l'hôte depuis Printing. Vrai quand le ticket est parti vers une imprimante.</summary>
public interface IStorefrontTickets
{
    Task<bool> PrintAsync(TenantId tenantId, LocationId locationId, string reference, KioskTicket ticket, CancellationToken cancellationToken = default);
}

// ---- Livraison et coupons (phase 7)

public sealed record StorefrontDeliveryZone(Guid ZoneId, string Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes);

/// <param name="LeadMinutes">Délai avant le départ du livreur ; le trajet est le délai de la zone.</param>
public sealed record StorefrontDeliveryOffer(int LeadMinutes, IReadOnlyList<StorefrontDeliveryZone> Zones);

/// <summary>Où en est la livraison, vu par le client : ni adresse, ni téléphone, ni livreur.</summary>
public sealed record StorefrontDeliveryTracking(string ZoneName, decimal Fee, string Status, DateTimeOffset PromisedAtUtc);

/// <summary>
/// La livraison, fournie par l'hôte depuis Delivery. Fail-closed : un tenant autre que celui du contexte n'a ni offre ni
/// fiche.
/// </summary>
public interface IStorefrontDelivery
{
    /// <summary>Les zones actives de l'établissement ; null quand la livraison n'y est pas activée.</summary>
    Task<StorefrontDeliveryOffer?> OfferAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>La fiche de livraison active de la commande ; null s'il n'y en a pas.</summary>
    Task<StorefrontDeliveryTracking?> TrackAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <summary>Ce que vaudrait un coupon sur le panier. Refusé : <paramref name="RefusalCode"/> dit pourquoi.</summary>
public sealed record CouponPreview(string Code, bool IsValid, string? RefusalCode, Guid? PromotionId, string? Label, decimal Discount);

/// <summary>Les coupons, fournis par l'hôte depuis Promotions. Fail-closed sur le tenant (coupon refusé).</summary>
public interface IStorefrontCoupons
{
    Task<CouponPreview> PreviewAsync(
        TenantId tenantId, LocationId locationId, ServiceMode mode, string currencyCode, IReadOnlyList<CartQuoteLine> lines, string code,
        CancellationToken cancellationToken = default);
}

// ---- Comptes clients (phase 8)

/// <summary>Où le client reçoit son code de connexion.</summary>
public enum CustomerSignInChannel
{
    Sms = 1,
    WhatsApp = 2,
    Email = 3,
}

public sealed record OtpChallengeIssued(Guid ChallengeId, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Les codes à usage unique, fournis par l'hôte depuis Notifications (envoi, limites de demandes, empreinte seule du
/// code). Fail-closed : un tenant autre que celui du contexte ne peut rien demander ni vérifier.
/// </summary>
public interface IStorefrontOtp
{
    /// <summary>Envoie un code ; lève l'erreur de Notifications (trop de demandes, canal non configuré...).</summary>
    Task<OtpChallengeIssued> RequestAsync(
        TenantId tenantId, CustomerSignInChannel channel, string address, string? language, CancellationToken cancellationToken = default);

    /// <summary>Vérifie le code du défi ; lève l'erreur de Notifications (faux, expiré, épuisé, déjà utilisé).</summary>
    Task VerifyAsync(TenantId tenantId, Guid challengeId, string code, CancellationToken cancellationToken = default);
}

public sealed record LoyaltyRewardOption(Guid RewardId, string Name, int PointCost, decimal Discount, bool Affordable);

/// <param name="Enabled">Le restaurant a un programme de fidélité actif.</param>
/// <param name="Rewards">Les récompenses actives dans la devise du restaurant.</param>
public sealed record CustomerLoyalty(bool Enabled, long Balance, decimal PointsPerCurrencyUnit, IReadOnlyList<LoyaltyRewardOption> Rewards);

/// <summary>La fidélité, fournie par l'hôte depuis Loyalty. Fail-closed : rien pour un autre tenant.</summary>
public interface IStorefrontLoyalty
{
    Task<CustomerLoyalty> SummaryAsync(TenantId tenantId, CustomerId customerId, string currencyCode, CancellationToken cancellationToken = default);
}

public interface ICustomerSessionRepository
{
    Task<CustomerSession?> FindAsync(TenantId tenantId, CustomerSessionId id, CancellationToken cancellationToken = default);

    void Add(CustomerSession session);
}

/// <summary>Retrouve une session par l'empreinte de son jeton, TOUS tenants confondus (authentification). Lecture seule.</summary>
public interface ICustomerSessionLookup
{
    Task<CustomerSession?> FindByTokenHashAsync(PublishableKeyHash tokenHash, CancellationToken cancellationToken = default);
}

/// <summary>Une application de vente vue par la plateforme : ni clé, ni empreinte, ni origine.</summary>
public sealed record PlatformSalesApp(
    TenantId TenantId, SalesAppId Id, string Name, SalesAppKind Kind, SalesAppStatus Status, DateTimeOffset CreatedAtUtc);

/// <summary>Une borne vue par la plateforme : ni jeton ni empreinte.</summary>
public sealed record PlatformKiosk(
    TenantId TenantId, KioskDeviceId Id, SalesAppId AppId, LocationId LocationId, string Name, KioskDeviceStatus Status,
    DateTimeOffset PairedAtUtc, DateTimeOffset? RevokedAtUtc);

/// <summary>Nombre de commandes en ligne d'un restaurant, par canal, mode de paiement et état, sur une période.</summary>
public sealed record PlatformOrderCount(TenantId TenantId, SalesAppKind Channel, PaymentMode Payment, OnlineOrderState State, int Count);

/// <summary>Dernière commande en ligne d'un restaurant (toutes périodes).</summary>
public sealed record PlatformLastOrder(TenantId TenantId, DateTimeOffset PlacedAtUtc);

/// <summary>Réglages de la vente en ligne d'un établissement, vus par la plateforme.</summary>
public sealed record PlatformLocationSettings(
    TenantId TenantId, LocationId LocationId, bool IsPaused, IReadOnlyList<ServiceMode> ServiceModes, int? SlotCapacity, DateTimeOffset UpdatedAtUtc);

/// <summary>Sessions clients d'un restaurant : actives à l'instant donné, et ouvertes sur la période.</summary>
public sealed record PlatformCustomerSessions(TenantId TenantId, int Active, int Opened);

/// <summary>
/// Pour le back-office de la plateforme SEULEMENT : tous les restaurants, lecture seule et déclarée (sans filtre de
/// tenant). Jamais utilisé par une route d'un restaurant ni d'une application de vente. Aucun secret (clé, jeton,
/// empreinte) ni aucune donnée personnelle n'en sort : des comptages, des noms d'applications et de bornes.
/// </summary>
public interface IOnlineSalesPlatform
{
    Task<IReadOnlyList<PlatformSalesApp>> AppsAsync(TenantId? tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformKiosk>> KiosksAsync(TenantId? tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformOrderCount>> OrdersAsync(
        TenantId? tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformLastOrder>> LastOrdersAsync(TenantId? tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformLocationSettings>> SettingsAsync(TenantId? tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlatformCustomerSessions>> CustomerSessionsAsync(
        TenantId? tenantId, DateTimeOffset nowUtc, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public sealed record StorefrontSurveyQuestion(Guid Id, int Order, string Text, string Type, bool IsRequired, IReadOnlyList<string> Choices);

/// <param name="IsOpen">Faux pour un questionnaire clos : il s'affiche encore, sans recevoir de réponse.</param>
public sealed record StorefrontSurvey(Guid Id, string Title, bool IsOpen, IReadOnlyList<StorefrontSurveyQuestion> Questions);

public sealed record StorefrontSurveyAnswer(Guid QuestionId, int? Rating = null, string? Text = null, IReadOnlyList<string>? Choices = null);

/// <summary>
/// Les questionnaires du restaurant (module Reviews, via l'hôte) : un questionnaire publié ou clos (jamais un brouillon),
/// celui proposé après une commande, et le dépôt d'une réponse anonyme.
/// </summary>
public interface IStorefrontSurveys
{
    Task<StorefrontSurvey?> FindAsync(TenantId tenantId, Guid surveyId, CancellationToken cancellationToken = default);

    Task<StorefrontSurvey?> ActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task SubmitAsync(
        TenantId tenantId, Guid surveyId, OrderId? orderId, Guid? locationId, IReadOnlyList<StorefrontSurveyAnswer> answers,
        CancellationToken cancellationToken = default);
}

/// <summary>Un avis publié par le restaurant : note, commentaire et date (jamais l'auteur).</summary>
public sealed record StorefrontReview(Guid Id, Guid LocationId, int Rating, string? Comment, DateTimeOffset SubmittedAtUtc);

public sealed record StorefrontRatingCount(int Rating, int Count);

/// <param name="AverageRating">Moyenne de tous les avis non archivés (deux décimales), null sans avis.</param>
/// <param name="Count">Nombre de ces avis ; <paramref name="CountByRating"/> les répartit de 1 à 5.</param>
/// <param name="Reviews">Les avis publiés par le restaurant, du plus récent au plus ancien.</param>
public sealed record StorefrontReviews(int Count, decimal? AverageRating, IReadOnlyList<StorefrontRatingCount> CountByRating, IReadOnlyList<StorefrontReview> Reviews);

/// <summary>Les avis du restaurant (module Reviews, via l'hôte) : note globale et avis publiés, pour ces établissements (null : tous).</summary>
public interface IStorefrontReviews
{
    Task<StorefrontReviews> GetAsync(TenantId tenantId, IReadOnlyList<LocationId>? locations, int take, CancellationToken cancellationToken = default);

    /// <summary>L'avis du client sur une commande (une seule fois) ; l'auteur est le client de la commande. Renvoie l'id de l'avis.</summary>
    Task<Guid> SubmitAsync(TenantId tenantId, LocationId locationId, OrderId orderId, int rating, string? comment, CancellationToken cancellationToken = default);
}

/// <param name="AcceptsWaitlist">Les clients peuvent s'inscrire en liste d'attente quand un créneau est complet.</param>
/// <param name="Deposit">La règle d'acompte, pour l'annoncer avant la réservation ; null sans acompte.</param>
public sealed record StorefrontBookingRules(
    bool AcceptsOnlineBookings, int MinLeadTimeMinutes, int MaxAdvanceDays, int DurationMinutes, int CancellationCutoffMinutes, int MaxPartySize,
    bool AcceptsWaitlist = false, StorefrontDepositRule? Deposit = null);

/// <param name="PerGuest">Montant par couvert (sinon pour la réservation).</param>
/// <param name="MinPartySize">Demandé à partir de ce nombre de couverts.</param>
public sealed record StorefrontDepositRule(decimal Amount, string CurrencyCode, bool PerGuest, int MinPartySize, int PaymentWindowMinutes);

/// <param name="Status">AwaitingPayment, Paid, Applied, RefundRequested, Refunded, Forfeited ou Waived.</param>
public sealed record StorefrontDeposit(decimal Amount, string CurrencyCode, string Status, DateTimeOffset? DueBeforeUtc, bool HasPendingPayment);

/// <param name="Status">Pending, Confirmed, Seated, Completed, Cancelled ou NoShow.</param>
public sealed record StorefrontReservation(
    Guid Id, LocationId LocationId, string Status, DateTimeOffset StartAtUtc, int DurationMinutes, int PartySize, string GuestName,
    bool CanCancel, DateTimeOffset? CancelBeforeUtc, StorefrontDeposit? Deposit = null);

/// <param name="Status">Waiting, Offered, Booked, Expired ou Cancelled.</param>
/// <param name="OfferedStartUtc">Heure proposée tant que l'offre peut être acceptée.</param>
public sealed record StorefrontWaitlistEntry(
    Guid Id, LocationId LocationId, string Status, DateTimeOffset DesiredStartUtc, int FlexibilityMinutes, int PartySize, string GuestName,
    DateTimeOffset? OfferedStartUtc, DateTimeOffset? OfferExpiresAtUtc, Guid? ReservationId);

/// <summary>La réservation de table en ligne (module Reservations, via l'hôte), pour le tenant de la clé publique.</summary>
public interface IStorefrontReservations
{
    Task<StorefrontBookingRules> RulesAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(DateTimeOffset StartAtUtc, bool IsAvailable)>> CheckAsync(
        TenantId tenantId, LocationId locationId, int partySize, IReadOnlyList<DateTimeOffset> starts, CancellationToken cancellationToken = default);

    Task<Guid> BookAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset startAt, int partySize, string? name, string? phone, string? email, string? notes,
        CustomerId? customerId, CancellationToken cancellationToken = default);

    Task<StorefrontReservation?> FindAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default);

    Task CancelAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>Paie l'acompte en ligne (montant fixé par la réservation) ; remplace un paiement resté en attente.</summary>
    Task<StartedPayment> StartDepositAsync(TenantId tenantId, Guid reservationId, OnlinePaymentRequest request, CancellationToken cancellationToken = default);

    Task<Guid> JoinWaitlistAsync(
        TenantId tenantId, LocationId locationId, DateTimeOffset desiredStart, int partySize, int flexibilityMinutes, string? name, string? phone,
        string? email, CustomerId? customerId, CancellationToken cancellationToken = default);

    Task<StorefrontWaitlistEntry?> FindWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>Accepte la place proposée : renvoie l'id de la réservation créée.</summary>
    Task<Guid> AcceptWaitlistOfferAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default);

    Task CancelWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le contrôle anti-robot des applications de vente (Cloudflare Turnstile). La clé de site est publique ; la clé secrète
/// reste dans l'Infrastructure et n'est jamais renvoyée ni journalisée.
/// </summary>
public interface IHumanCheck
{
    /// <summary>Faux tant que la plateforme n'a pas configuré le service : il ne peut alors pas être exigé.</summary>
    bool IsAvailable { get; }

    /// <summary>La clé de site à donner au widget ; null si indisponible.</summary>
    string? SiteKey { get; }

    /// <summary>Vrai si le jeton du widget est valide (une seule utilisation, vérifié chez le prestataire).</summary>
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
}

// ---- Reçus

public sealed record ReceiptItem(int Quantity, string Name, string? Details, string Amount);

public sealed record ReceiptAmount(string Label, string Amount);

/// <summary>
/// Le reçu d'une commande (hors brouillon), formaté par l'hôte depuis Orders, Payments, Fiscal, Tenants et Locations :
/// aucune donnée personnelle du client (seulement l'id de sa fiche, pour lui envoyer le lien).
/// </summary>
public sealed record StorefrontReceipt(
    OrderId OrderId,
    LocationId LocationId,
    CustomerId? CustomerId,
    string RestaurantName,
    string LocationName,
    string? Address,
    string OrderNumber,
    string ServiceMode,
    string Date,
    bool IsCancelled,
    IReadOnlyList<ReceiptItem> Lines,
    IReadOnlyList<ReceiptAmount> Totals,
    string Total,
    IReadOnlyList<ReceiptAmount> Payments,
    string? AmountDue,
    IReadOnlyList<string> Footer,
    string? QrCode,
    bool PricesIncludeTax);

public interface IStorefrontReceipts
{
    /// <summary>Null pour une commande inconnue, d'un autre restaurant, ou en brouillon.</summary>
    Task<StorefrontReceipt?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

public enum ReceiptChannel
{
    WhatsApp = 1,
    Sms = 2,
}

/// <summary>
/// Le lien du reçu à envoyer : au numéro saisi, ou à la fiche client (son numéro est lu par le module de notification).
/// Le nom et le numéro ne sont jamais journalisés.
/// </summary>
public sealed record ReceiptMessage(
    LocationId LocationId,
    CustomerId? CustomerId,
    PhoneNumber? Phone,
    ReceiptChannel Channel,
    string RestaurantName,
    string LocationName,
    string OrderNumber,
    string Total,
    string Url,
    string IdempotencyKey)
{
    public override string ToString() => $"ReceiptMessage ({OrderNumber}, {Channel}, ***)";
}

/// <param name="SkipReason">Raison donnée par le module de notification quand rien n'est parti (NoProvider, NoTemplate...).</param>
public sealed record ReceiptMessageOutcome(bool Sent, string? SkipReason);

/// <summary>L'envoi du lien du reçu, fourni par l'hôte depuis Notifications (type <c>order.receipt</c>).</summary>
public interface IStorefrontReceiptMessages
{
    Task<ReceiptMessageOutcome> SendAsync(TenantId tenantId, ReceiptMessage message, CancellationToken cancellationToken = default);
}

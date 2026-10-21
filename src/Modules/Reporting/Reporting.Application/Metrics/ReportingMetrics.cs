using Reporting.Domain;

namespace Reporting.Application.Metrics;

/// <summary>Une mesure stockée (compteur additif du jour) ou dérivée (rapport de deux mesures stockées).</summary>
/// <param name="Monetary">Montant : toujours ventilé par devise, jamais additionné entre devises.</param>
/// <param name="Numerator">Mesure dérivée : numérateur (null pour une mesure stockée).</param>
/// <param name="Scale">Mesure dérivée : facteur appliqué au rapport (100 pour un pourcentage).</param>
/// <param name="Hourly">Mesure horaire : sa dimension est l'heure LOCALE de l'événement (« 00 » à « 23 »), fixée à l'enregistrement.</param>
public sealed record MetricDefinition(
    string Code, string Source, bool Monetary, string? Numerator = null, string? Denominator = null, decimal Scale = 1m, bool Hourly = false)
{
    public bool IsDerived => Numerator is not null;
}

/// <summary>
/// Le catalogue des mesures que les projections savent calculer. C'est la liste blanche passée au domaine
/// (<see cref="ReportCatalog"/>) : un rapport ne peut référencer rien d'autre. Les compteurs sont alimentés par les
/// événements des autres modules (traduits par l'hôte) ; une mesure dérivée se calcule à la lecture, après agrégation.
/// </summary>
public static class ReportingMetrics
{
    // Ventes (Orders). Dimension des commandes passées : le type de commande.
    public const string OrdersPlaced = "sales.orders_placed";
    public const string PlacedAmount = "sales.placed_amount";
    public const string DiscountAmount = "sales.discount_amount";
    public const string TaxAmount = "sales.tax_amount";
    public const string OrdersCompleted = "sales.orders_completed";
    public const string Revenue = "sales.revenue";

    /// <summary>TVA comprise dans le chiffre d'affaires (commandes terminées) : la base de l'export comptable.</summary>
    public const string RevenueTax = "sales.revenue_tax";
    public const string OrdersCancelled = "sales.orders_cancelled";
    public const string CancelledAmount = "sales.cancelled_amount";
    public const string AverageTicket = "sales.average_ticket";
    public const string CancellationRate = "sales.cancellation_rate";

    // Vente en ligne (commandes terminées venues du site, de l'application ou d'une borne). Dimension : le canal.
    public const string OnlineOrdersCompleted = "sales.online_orders_completed";
    public const string OnlineRevenue = "sales.online_revenue";

    // Commandes terminées par heure locale (dimension « 00 » à « 23 ») : avec le jour, la carte de chaleur des ventes.
    public const string HourlyOrders = "sales.hourly_orders";
    public const string HourlyRevenue = "sales.hourly_revenue";

    // Ventes par produit (commandes terminées). Dimension : l'id du produit.
    public const string ItemsSold = "sales.items_sold";
    public const string ProductRevenue = "sales.product_revenue";

    // Paiements (Payments). Dimension : le moyen de paiement.
    public const string PaymentsCaptured = "payments.captured_count";
    public const string CapturedAmount = "payments.captured_amount";
    public const string PaymentsFailed = "payments.failed_count";
    public const string Refunds = "payments.refund_count";
    public const string RefundedAmount = "payments.refunded_amount";
    public const string RefundRate = "payments.refund_rate";

    // Réservations. Dimension des annulations : qui a annulé.
    public const string ReservationsCreated = "reservations.created";
    public const string CoversBooked = "reservations.covers_booked";
    public const string ReservationsSeated = "reservations.seated";
    public const string CoversSeated = "reservations.covers_seated";
    public const string ReservationsCancelled = "reservations.cancelled";
    public const string NoShows = "reservations.no_shows";
    public const string NoShowRate = "reservations.no_show_rate";

    // Cuisine.
    public const string TicketsCreated = "kitchen.tickets_created";
    public const string TicketsReady = "kitchen.tickets_ready";
    public const string TicketsCancelled = "kitchen.tickets_cancelled";
    public const string PreparationSeconds = "kitchen.preparation_seconds";
    public const string AveragePreparationMinutes = "kitchen.average_preparation_minutes";

    // Stock : des comptages d'événements (les quantités, dans des unités différentes, ne s'additionnent pas).
    public const string StockReceipts = "inventory.receipts";
    public const string WasteRecords = "inventory.waste_records";
    public const string StockAdjustments = "inventory.adjustments";
    public const string LowStockAlerts = "inventory.low_stock_alerts";
    public const string NegativeStockAlerts = "inventory.negative_stock_alerts";

    // Promotions.
    public const string PromotionsApplied = "promotions.applied";
    public const string PromotionDiscountAmount = "promotions.discount_amount";
    public const string PromotionsReverted = "promotions.reverted";
    public const string CouponsRedeemed = "promotions.coupons_redeemed";

    // Fidélité (une reprise de points est comptée en négatif).
    public const string LoyaltyAccountsOpened = "loyalty.accounts_opened";
    public const string PointsEarned = "loyalty.points_earned";
    public const string PointsRedeemed = "loyalty.points_redeemed";
    public const string PointsExpired = "loyalty.points_expired";
    public const string RewardsRedeemed = "loyalty.rewards_redeemed";
    public const string RewardDiscountAmount = "loyalty.reward_discount_amount";

    /// <summary>Champs de filtre acceptés (opérateurs : égal, différent, dans une liste).</summary>
    public const string LocationField = "location.id";
    public const string CurrencyField = "currency";
    public const string DimensionField = "dimension";

    private static readonly MetricDefinition[] Definitions =
    [
        new(OrdersPlaced, "orders", false),
        new(PlacedAmount, "orders", true),
        new(DiscountAmount, "orders", true),
        new(TaxAmount, "orders", true),
        new(OrdersCompleted, "orders", false),
        new(Revenue, "orders", true),
        new(RevenueTax, "orders", true),
        new(OrdersCancelled, "orders", false),
        new(CancelledAmount, "orders", true),
        new(AverageTicket, "orders", true, Revenue, OrdersCompleted),
        new(CancellationRate, "orders", false, OrdersCancelled, OrdersPlaced, 100m),
        new(OnlineOrdersCompleted, "orders", false),
        new(OnlineRevenue, "orders", true),
        new(HourlyOrders, "orders", false, Hourly: true),
        new(HourlyRevenue, "orders", true, Hourly: true),
        new(ItemsSold, "orders", false),
        new(ProductRevenue, "orders", true),

        new(PaymentsCaptured, "payments", false),
        new(CapturedAmount, "payments", true),
        new(PaymentsFailed, "payments", false),
        new(Refunds, "payments", false),
        new(RefundedAmount, "payments", true),
        new(RefundRate, "payments", false, RefundedAmount, CapturedAmount, 100m),

        new(ReservationsCreated, "reservations", false),
        new(CoversBooked, "reservations", false),
        new(ReservationsSeated, "reservations", false),
        new(CoversSeated, "reservations", false),
        new(ReservationsCancelled, "reservations", false),
        new(NoShows, "reservations", false),
        new(NoShowRate, "reservations", false, NoShows, ReservationsCreated, 100m),

        new(TicketsCreated, "kitchen", false),
        new(TicketsReady, "kitchen", false),
        new(TicketsCancelled, "kitchen", false),
        new(PreparationSeconds, "kitchen", false),
        new(AveragePreparationMinutes, "kitchen", false, PreparationSeconds, TicketsReady, 1m / 60m),

        new(StockReceipts, "inventory", false),
        new(WasteRecords, "inventory", false),
        new(StockAdjustments, "inventory", false),
        new(LowStockAlerts, "inventory", false),
        new(NegativeStockAlerts, "inventory", false),

        new(PromotionsApplied, "promotions", false),
        new(PromotionDiscountAmount, "promotions", true),
        new(PromotionsReverted, "promotions", false),
        new(CouponsRedeemed, "promotions", false),

        new(LoyaltyAccountsOpened, "loyalty", false),
        new(PointsEarned, "loyalty", false),
        new(PointsRedeemed, "loyalty", false),
        new(PointsExpired, "loyalty", false),
        new(RewardsRedeemed, "loyalty", false),
        new(RewardDiscountAmount, "loyalty", true),
    ];

    private static readonly Dictionary<string, MetricDefinition> ByCode =
        Definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal);

    public static IReadOnlyList<MetricDefinition> All => Definitions;

    public static IReadOnlyList<string> FilterFields { get; } = [LocationField, CurrencyField, DimensionField];

    /// <summary>La liste blanche du domaine : toutes les mesures, stockées et dérivées, et les champs de filtre.</summary>
    public static ReportCatalog Catalog { get; } = ReportCatalog.Create(Definitions.Select(definition => definition.Code), FilterFields);

    public static MetricDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    public static MetricDefinition Require(string code) =>
        Find(code) ?? throw new DomainException(ReportingApplicationErrors.MetricUnknown);

    public static bool IsStored(string code) => Find(code) is { IsDerived: false };

    /// <summary>Les mesures stockées à lire pour calculer <paramref name="codes"/> (dérivées comprises).</summary>
    public static IReadOnlyList<string> StoredInputs(IEnumerable<string> codes) =>
        codes.Select(Require)
            .SelectMany(definition => definition.IsDerived ? new[] { definition.Numerator!, definition.Denominator! } : new[] { definition.Code })
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

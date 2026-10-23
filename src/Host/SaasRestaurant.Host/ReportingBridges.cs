using System.Globalization;
using SaasRestaurant.Contracts;
using Billing.Application.Queries;
using Kitchen.Application.Queries;
using Notifications.Application.Commands;
using Notifications.Domain;
using Orders.Application.Queries;
using Payments.Application.Queries;
using Reporting.Application.Abstractions;
using Reporting.Application.Commands;
using Reporting.Application.Platform;
using Reporting.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;
using InventoryContracts = SaasRestaurant.Contracts.Inventory;
using KitchenContracts = SaasRestaurant.Contracts.Kitchen;
using LoyaltyContracts = SaasRestaurant.Contracts.Loyalty;
using M = Reporting.Application.Metrics.ReportingMetrics;
using OrdersContracts = SaasRestaurant.Contracts.Orders;
using PaymentsContracts = SaasRestaurant.Contracts.Payments;
using PromotionsContracts = SaasRestaurant.Contracts.Promotions;
using ReservationsContracts = SaasRestaurant.Contracts.Reservations;

namespace SaasRestaurant.Host;

// Les ports déclarés par Reporting, servis par Tenants, et l'alimentation de ses projections : les événements des
// autres modules (par l'outbox) deviennent des incréments de compteurs du jour, et ceux de Tenants et Billing l'état
// de la plateforme. Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Reporting -> Tenants : nom, fuseau (jour local des compteurs) et acceptation des écritures. Inconnu = null.</summary>
internal sealed class ReportingTenantsFromTenants(CachedTenants tenants) : IReportingTenants
{
    public async Task<ReportingTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        return tenant is null
            ? null
            : new ReportingTenant(tenantId, tenant.Name, TimeZoneId.Create(tenant.TimeZoneId), tenant.CanAcceptBusinessWrites);
    }
}

/// <summary>
/// Alimente Reporting par l'outbox (après commit, au moins une fois), un scope DI par message pour le tenant du message
/// (<see cref="BackgroundTenant"/>). Chaque message devient au plus UN appel idempotent
/// (<see cref="RecordReportingFactsCommand"/>, clé = module et id du message) : un message rejoué ne compte pas deux
/// fois. Les compteurs ne contiennent que des nombres, des codes et des ids d'établissement : aucune donnée personnelle.
/// Quand un événement ne porte pas tout (établissement ou moyen d'un paiement, début d'un ticket), le module source est
/// relu pour le tenant du message. Un refus métier part en file morte ; les autres erreurs sont retentées.
/// </summary>
internal sealed class ReportingFeed(IServiceScopeFactory scopes) : IOutboxConsumer
{
    private static readonly Dictionary<(string Module, string Type), Func<OutboxEnvelope, IIntegrationEvent>> Readers = new()
    {
        [("orders", "OrderPlaced")] = message => message.Read<OrdersContracts.OrderPlaced>(),
        [("orders", "OrderCompleted")] = message => message.Read<OrdersContracts.OrderCompleted>(),
        [("orders", "OrderCancelled")] = message => message.Read<OrdersContracts.OrderCancelled>(),
        [("payments", "PaymentCaptured")] = message => message.Read<PaymentsContracts.PaymentCaptured>(),
        [("payments", "PaymentFailed")] = message => message.Read<PaymentsContracts.PaymentFailed>(),
        [("payments", "RefundSucceeded")] = message => message.Read<PaymentsContracts.RefundSucceeded>(),
        [("reservations", "ReservationCreated")] = message => message.Read<ReservationsContracts.ReservationCreated>(),
        [("reservations", "ReservationPartySizeChanged")] = message => message.Read<ReservationsContracts.ReservationPartySizeChanged>(),
        [("reservations", "GuestSeated")] = message => message.Read<ReservationsContracts.GuestSeated>(),
        [("reservations", "ReservationCancelled")] = message => message.Read<ReservationsContracts.ReservationCancelled>(),
        [("reservations", "ReservationNoShow")] = message => message.Read<ReservationsContracts.ReservationNoShow>(),
        [("kitchen", "KitchenTicketCreated")] = message => message.Read<KitchenContracts.KitchenTicketCreated>(),
        [("kitchen", "KitchenTicketReady")] = message => message.Read<KitchenContracts.KitchenTicketReady>(),
        [("kitchen", "KitchenTicketCancelled")] = message => message.Read<KitchenContracts.KitchenTicketCancelled>(),
        [("inventory", "StockReceived")] = message => message.Read<InventoryContracts.StockReceived>(),
        [("inventory", "WasteRecorded")] = message => message.Read<InventoryContracts.WasteRecorded>(),
        [("inventory", "StockAdjusted")] = message => message.Read<InventoryContracts.StockAdjusted>(),
        [("inventory", "LowStockReached")] = message => message.Read<InventoryContracts.LowStockReached>(),
        [("inventory", "StockWentNegative")] = message => message.Read<InventoryContracts.StockWentNegative>(),
        [("promotions", "PromotionApplied")] = message => message.Read<PromotionsContracts.PromotionApplied>(),
        [("promotions", "PromotionUsageReverted")] = message => message.Read<PromotionsContracts.PromotionUsageReverted>(),
        [("promotions", "CouponRedeemed")] = message => message.Read<PromotionsContracts.CouponRedeemed>(),
        [("promotions", "CouponReleased")] = message => message.Read<PromotionsContracts.CouponReleased>(),
        [("loyalty", "LoyaltyAccountOpened")] = message => message.Read<LoyaltyContracts.LoyaltyAccountOpened>(),
        [("loyalty", "PointsEarned")] = message => message.Read<LoyaltyContracts.PointsEarned>(),
        [("loyalty", "PointsRedeemed")] = message => message.Read<LoyaltyContracts.PointsRedeemed>(),
        [("loyalty", "RewardRedeemed")] = message => message.Read<LoyaltyContracts.RewardRedeemed>(),
        [("loyalty", "PointsExpired")] = message => message.Read<LoyaltyContracts.PointsExpired>(),
        [("loyalty", "PointsEarnReversed")] = message => message.Read<LoyaltyContracts.PointsEarnReversed>(),
        [("loyalty", "PointsRedeemReversed")] = message => message.Read<LoyaltyContracts.PointsRedeemReversed>(),
    };

    internal static IReadOnlyCollection<(string Module, string Type)> Handled => Readers.Keys;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Readers.TryGetValue((message.Module, message.Type), out var read))
        {
            return;
        }

        var integrationEvent = read(message);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(integrationEvent.TenantId));

        var (location, facts) = await TranslateAsync(services, integrationEvent, message.OccurredAtUtc, cancellationToken);
        if (facts.Count == 0)
        {
            return;
        }

        _ = await services.GetRequiredService<ICommandHandler<RecordReportingFactsCommand, bool>>().HandleAsync(
            new RecordReportingFactsCommand($"{message.Module}:{message.Id}", message.OccurredAtUtc, location, facts),
            cancellationToken);
    }

    private static async Task<(Guid? Location, List<ReportingFactInput> Facts)> TranslateAsync(
        IServiceProvider services, IIntegrationEvent integrationEvent, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var location = integrationEvent is ILocatedIntegrationEvent located ? located.LocationId : (Guid?)null;
        var facts = new List<ReportingFactInput>();
        switch (integrationEvent)
        {
            case OrdersContracts.OrderPlaced placed:
            {
                var orderType = Code(placed.OrderType);
                var currency = placed.Total.Currency;
                facts.Add(new(M.OrdersPlaced, 1m, orderType, currency));
                facts.Add(new(M.PlacedAmount, placed.Total.Amount, orderType, currency));
                facts.Add(new(M.DiscountAmount, placed.DiscountTotal.Amount, null, currency));
                facts.Add(new(M.TaxAmount, placed.TaxTotal.Amount, null, currency));
                break;
            }

            case OrdersContracts.OrderCompleted completed:
            {
                facts.Add(new(M.OrdersCompleted, 1m, null, completed.Total.Currency));
                facts.Add(new(M.Revenue, completed.Total.Amount, null, completed.Total.Currency));
                facts.Add(new(M.HourlyOrders, 1m, null, completed.Total.Currency));
                facts.Add(new(M.HourlyRevenue, completed.Total.Amount, null, completed.Total.Currency));
                var order = await services.GetRequiredService<IQueryHandler<GetOrderByIdQuery, OrderDto>>()
                    .HandleAsync(new GetOrderByIdQuery(completed.OrderId), cancellationToken);
                facts.AddRange(ProductFacts(order.Items, order.CurrencyCode));
                facts.Add(new(M.RevenueTax, order.TaxTotal, null, completed.Total.Currency));
                if (order.Channel is Orders.Domain.OrderChannel.Web or Orders.Domain.OrderChannel.MobileApp or Orders.Domain.OrderChannel.Kiosk
                    or Orders.Domain.OrderChannel.DeliveryPlatform)
                {
                    // Une plateforme de livraison compte sous son code (glovo, gozem...).
                    var channel = order.Channel == Orders.Domain.OrderChannel.DeliveryPlatform && order.Platform is { } platform
                        ? platform
                        : Code(order.Channel.ToString());
                    facts.Add(new(M.OnlineOrdersCompleted, 1m, channel, completed.Total.Currency));
                    facts.Add(new(M.OnlineRevenue, completed.Total.Amount, channel, completed.Total.Currency));
                }
                break;
            }

            case OrdersContracts.OrderCancelled cancelled:
                facts.Add(new(M.OrdersCancelled, 1m, null, cancelled.Total.Currency));
                facts.Add(new(M.CancelledAmount, cancelled.Total.Amount, null, cancelled.Total.Currency));
                break;

            case PaymentsContracts.PaymentCaptured captured:
            {
                var payment = await PaymentAsync(services, captured.PaymentId, cancellationToken);
                facts.Add(new(M.PaymentsCaptured, 1m, Code(payment.Method.ToString()), captured.CapturedAmount.Currency));
                facts.Add(new(M.CapturedAmount, captured.CapturedAmount.Amount, Code(payment.Method.ToString()), captured.CapturedAmount.Currency));
                location = payment.LocationId;
                break;
            }

            case PaymentsContracts.PaymentFailed failed:
            {
                var payment = await PaymentAsync(services, failed.PaymentId, cancellationToken);
                facts.Add(new(M.PaymentsFailed, 1m, Code(payment.Method.ToString()), payment.CurrencyCode));
                location = payment.LocationId;
                break;
            }

            case PaymentsContracts.RefundSucceeded refunded:
            {
                var payment = await PaymentAsync(services, refunded.PaymentId, cancellationToken);
                facts.Add(new(M.Refunds, 1m, Code(payment.Method.ToString()), refunded.Amount.Currency));
                facts.Add(new(M.RefundedAmount, refunded.Amount.Amount, Code(payment.Method.ToString()), refunded.Amount.Currency));
                location = payment.LocationId;
                break;
            }

            case ReservationsContracts.ReservationCreated created:
                facts.Add(new(M.ReservationsCreated, 1m));
                facts.Add(new(M.CoversBooked, created.PartySize));
                break;
            case ReservationsContracts.ReservationPartySizeChanged changed:
                facts.Add(new(M.CoversBooked, changed.PartySize - changed.PreviousPartySize));
                break;
            case ReservationsContracts.GuestSeated seated:
                facts.Add(new(M.ReservationsSeated, 1m));
                facts.Add(new(M.CoversSeated, seated.PartySize));
                break;
            case ReservationsContracts.ReservationCancelled cancelledReservation:
                facts.Add(new(M.ReservationsCancelled, 1m, Code(cancelledReservation.CancelledBy)));
                break;
            case ReservationsContracts.ReservationNoShow:
                facts.Add(new(M.NoShows, 1m));
                break;

            case KitchenContracts.KitchenTicketCreated:
                facts.Add(new(M.TicketsCreated, 1m));
                break;
            case KitchenContracts.KitchenTicketReady ready:
            {
                var ticket = await services.GetRequiredService<IQueryHandler<GetTicketByIdQuery, KitchenTicketDto>>()
                    .HandleAsync(new GetTicketByIdQuery(ready.TicketId), cancellationToken);
                facts.Add(new(M.TicketsReady, 1m));
                facts.Add(new(M.PreparationSeconds, Math.Max(0m, (decimal)Math.Round((occurredAtUtc - ticket.CreatedAtUtc).TotalSeconds))));
                break;
            }

            case KitchenContracts.KitchenTicketCancelled:
                facts.Add(new(M.TicketsCancelled, 1m));
                break;

            case InventoryContracts.StockReceived:
                facts.Add(new(M.StockReceipts, 1m));
                break;
            case InventoryContracts.WasteRecorded:
                facts.Add(new(M.WasteRecords, 1m));
                break;
            case InventoryContracts.StockAdjusted:
                facts.Add(new(M.StockAdjustments, 1m));
                break;
            case InventoryContracts.LowStockReached:
                facts.Add(new(M.LowStockAlerts, 1m));
                break;
            case InventoryContracts.StockWentNegative:
                facts.Add(new(M.NegativeStockAlerts, 1m));
                break;

            case PromotionsContracts.PromotionApplied applied:
                facts.Add(new(M.PromotionsApplied, 1m));
                facts.Add(new(M.PromotionDiscountAmount, applied.Discount.Amount, null, applied.Discount.Currency));
                break;
            case PromotionsContracts.PromotionUsageReverted:
                facts.Add(new(M.PromotionsReverted, 1m));
                break;
            case PromotionsContracts.CouponRedeemed:
                facts.Add(new(M.CouponsRedeemed, 1m));
                break;
            case PromotionsContracts.CouponReleased:
                facts.Add(new(M.CouponsRedeemed, -1m));
                break;

            case LoyaltyContracts.LoyaltyAccountOpened:
                facts.Add(new(M.LoyaltyAccountsOpened, 1m));
                break;
            case LoyaltyContracts.PointsEarned earned:
                facts.Add(new(M.PointsEarned, earned.Points));
                break;
            case LoyaltyContracts.PointsEarnReversed earnReversed:
                facts.Add(new(M.PointsEarned, -earnReversed.Points));
                break;
            case LoyaltyContracts.PointsRedeemed redeemed:
                facts.Add(new(M.PointsRedeemed, redeemed.Points));
                break;
            case LoyaltyContracts.PointsRedeemReversed redeemReversed:
                facts.Add(new(M.PointsRedeemed, -redeemReversed.Points));
                break;
            case LoyaltyContracts.PointsExpired expired:
                facts.Add(new(M.PointsExpired, expired.Points));
                break;
            case LoyaltyContracts.RewardRedeemed reward:
                facts.Add(new(M.RewardsRedeemed, 1m));
                facts.Add(new(M.PointsRedeemed, reward.Points));
                facts.Add(new(M.RewardDiscountAmount, reward.DiscountValue.Amount, null, reward.DiscountValue.Currency));
                break;
        }

        return (location, facts);
    }

    /// <summary>
    /// Quantités et montants TTC remisés d'une commande terminée, une ligne par produit (dimension : son id). Les
    /// variantes et options sont comptées avec leur produit.
    /// </summary>
    internal static IEnumerable<ReportingFactInput> ProductFacts(IEnumerable<OrderItemDto> items, string currency) =>
        items
            .Where(item => item.Quantity > 0)
            .GroupBy(item => item.ProductId)
            .SelectMany(group => new[]
            {
                new ReportingFactInput(M.ItemsSold, group.Sum(item => item.Quantity), group.Key.ToString("D"), currency),
                new ReportingFactInput(M.ProductRevenue, group.Sum(item => item.TotalWithTax), group.Key.ToString("D"), currency),
            });

    private static Task<PaymentDto> PaymentAsync(IServiceProvider services, Guid paymentId, CancellationToken cancellationToken) =>
        services.GetRequiredService<IQueryHandler<GetPaymentByIdQuery, PaymentDto>>()
            .HandleAsync(new GetPaymentByIdQuery(paymentId), cancellationToken);

    /// <summary>Un nom d'enum devenu code de dimension : <c>MobileMoney</c> -> <c>mobile_money</c>.</summary>
    internal static string Code(string? name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (character, index) in (name ?? string.Empty).Select((c, i) => (c, i)))
        {
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}

/// <summary>
/// La vue de la plateforme dans Reporting, par l'outbox : à chaque événement d'un restaurant (Tenants) ou de son
/// abonnement (Billing), l'état COURANT est relu dans le module source et recopié (le dernier fait foi).
/// </summary>
internal sealed class ReportingPlatformFeed(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var isTenant = message.Module == "tenants" && message.Type.StartsWith("Tenant", StringComparison.Ordinal);
        var isSubscription = message.Module == "billing" && message.Type.StartsWith("Subscription", StringComparison.Ordinal);
        var isAddOn = message.Module == "billing" && message.Type.StartsWith("TenantAddOn", StringComparison.Ordinal);
        if (!isTenant && !isSubscription && !isAddOn)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (isTenant)
        {
            var tenant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
                .HandleAsync(new GetTenantByIdQuery(message.Read<SaasRestaurant.Contracts.Tenants.TenantEvent>().TenantId), cancellationToken);
            await services.GetRequiredService<ICommandHandler<RecordTenantSnapshotCommand>>().HandleAsync(
                new RecordTenantSnapshotCommand(tenant.Id, tenant.Name, TenantStatusOf(tenant.Status), tenant.CreatedAtUtc), cancellationToken);
            return;
        }

        Guid subscriptionId;
        if (isSubscription)
        {
            subscriptionId = message.Read<SaasRestaurant.Contracts.Billing.SubscriptionEvent>().SubscriptionId;
        }
        else
        {
            // Une option change le montant de l'abonnement vivant du restaurant (s'il en a un).
            var tenantId = message.Read<SaasRestaurant.Contracts.Billing.TenantAddOnEvent>().TenantId;
            var live = (await services.GetRequiredService<IQueryHandler<ListSubscriptionsQuery, IReadOnlyList<SubscriptionDto>>>()
                    .HandleAsync(new ListSubscriptionsQuery(tenantId, null, 0, 10), cancellationToken))
                .FirstOrDefault(subscription => subscription.Status != Billing.Domain.SubscriptionStatus.Cancelled);
            if (live is null)
            {
                return;
            }

            subscriptionId = live.Id;
        }

        await RecordSubscriptionAsync(services, subscriptionId, cancellationToken);
    }

    // Le montant mensuel recopié dans Reporting (MRR) : plan et options actives, chacun ramené au mois.
    private static async Task RecordSubscriptionAsync(IServiceProvider services, Guid subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await services.GetRequiredService<IQueryHandler<GetSubscriptionQuery, SubscriptionDto>>()
            .HandleAsync(new GetSubscriptionQuery(subscriptionId), cancellationToken);
        var plan = await services.GetRequiredService<IQueryHandler<GetPlanQuery, PlanDto>>()
            .HandleAsync(new GetPlanQuery(subscription.PlanId), cancellationToken);
        var charge = await services.GetRequiredService<IQueryHandler<GetSubscriptionChargeQuery, SubscriptionChargeDto>>()
            .HandleAsync(new GetSubscriptionChargeQuery(subscriptionId), cancellationToken);
        await services.GetRequiredService<ICommandHandler<RecordSubscriptionSnapshotCommand>>().HandleAsync(
            new RecordSubscriptionSnapshotCommand(
                subscription.TenantId,
                subscription.Id,
                plan.Id,
                plan.Name,
                SubscriptionStatusOf(subscription.Status),
                subscription.IsComplimentary ? 0 : charge.MonthlyTotal,
                plan.CurrencyCode,
                false),
            cancellationToken);
    }

    private static PlatformTenantStatus TenantStatusOf(Tenants.Domain.Tenants.TenantStatus status) => status switch
    {
        Tenants.Domain.Tenants.TenantStatus.Trial => PlatformTenantStatus.Trial,
        Tenants.Domain.Tenants.TenantStatus.Active => PlatformTenantStatus.Active,
        Tenants.Domain.Tenants.TenantStatus.Suspended => PlatformTenantStatus.Suspended,
        _ => PlatformTenantStatus.Closed,
    };

    private static PlatformSubscriptionStatus SubscriptionStatusOf(Billing.Domain.SubscriptionStatus status) => status switch
    {
        Billing.Domain.SubscriptionStatus.Trialing => PlatformSubscriptionStatus.Trialing,
        Billing.Domain.SubscriptionStatus.Active => PlatformSubscriptionStatus.Active,
        Billing.Domain.SubscriptionStatus.PastDue => PlatformSubscriptionStatus.PastDue,
        Billing.Domain.SubscriptionStatus.Suspended => PlatformSubscriptionStatus.Suspended,
        _ => PlatformSubscriptionStatus.Cancelled,
    };
}

/// <summary>
/// Un export terminé (reporting.ExportCompleted, par l'outbox) est annoncé par Notifications (type
/// <c>staff.report_ready</c>) : un e-mail par destinataire pour un envoi planifié, sinon une notification dans
/// l'application au demandeur (rien pour une clé d'API : elle suit l'export par GET /reporting/exports/{id}). Le lien mène à la page « Exports récents » de la console
/// (<c>Reporting:PublicBaseUrl</c> = adresse de la console), où l'export se télécharge connecté : le fichier ne voyage jamais
/// dans la notification. Clés d'idempotence : un rejeu n'envoie rien de plus.
/// </summary>
internal sealed class ReportingDeliveryFlow(IServiceScopeFactory scopes, IConfiguration configuration)
    : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("reporting", "ExportCompleted"))
        {
            return;
        }

        var completed = message.Read<SaasRestaurant.Contracts.Reporting.ExportCompleted>();
        var tenantId = TenantId.From(completed.TenantId);
        var jobId = completed.ExportJobId;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var delivery = await services.GetRequiredService<IQueryHandler<GetExportDeliveryQuery, ExportDeliveryDto>>()
            .HandleAsync(new GetExportDeliveryQuery(jobId), cancellationToken);
        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenantId.Value), cancellationToken);
        var baseUrl = (configuration["Reporting:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["restaurant.name"] = restaurant.Name,
            ["report.name"] = delivery.Title,
            ["report.period"] = string.Create(CultureInfo.InvariantCulture, $"{delivery.From:yyyy-MM-dd} - {delivery.To:yyyy-MM-dd}"),
            ["report.url"] = $"{baseUrl}/rapports/planifies",
        };

        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        if (delivery.Recipients.Count > 0)
        {
            foreach (var recipient in delivery.Recipients)
            {
                _ = await send.HandleAsync(
                    new SendNotificationCommand(
                        NotificationTypes.StaffReportReady.Key.Value, NotificationChannel.Email, Address: recipient, Variables: variables,
                        IdempotencyKey: $"reporting:{jobId}:{recipient}"),
                    cancellationToken);
            }
        }
        else if (delivery.RequestedBy is { } requester)
        {
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    NotificationTypes.StaffReportReady.Key.Value, NotificationChannel.InApp, UserId: requester, Variables: variables,
                    IdempotencyKey: $"reporting:{jobId}"),
                cancellationToken);
        }
    }
}

/// <summary>Reporting -> Locations : les noms des établissements du tenant du contexte (pour les exports PDF et Excel).</summary>
internal sealed class ReportingLocationNamesFromLocations(
    ITenantContext tenantContext, IQueryHandler<Locations.Application.Queries.ListLocationsQuery, PagedResult<Locations.Application.Queries.LocationSummaryDto>> locations)
    : IReportingLocationNames
{
    public async Task<IReadOnlyDictionary<Guid, string>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return new Dictionary<Guid, string>();
        }

        var page = await locations.HandleAsync(
            new Locations.Application.Queries.ListLocationsQuery(1, Locations.Application.Queries.ListLocationsQuery.MaxPageSize, IncludeArchived: true), cancellationToken);
        return page.Items.ToDictionary(location => location.Id, location => location.Name);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Integrations.Application;
using Integrations.Application.Webhooks;
using Notifications.Application.Commands;
using Notifications.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Queries;
using Staff.Domain;
using Tenants.Application.Queries;
using SaasRestaurant.Contracts;
using OrdersContracts = SaasRestaurant.Contracts.Orders;
using PaymentsContracts = SaasRestaurant.Contracts.Payments;
using ReservationsContracts = SaasRestaurant.Contracts.Reservations;
using KitchenContracts = SaasRestaurant.Contracts.Kitchen;
using InventoryContracts = SaasRestaurant.Contracts.Inventory;
using CatalogContracts = SaasRestaurant.Contracts.Catalog;

namespace SaasRestaurant.Host;

// Les ports déclarés par Integrations, servis par Tenants, et la publication des webhooks : les événements des autres
// modules (par l'outbox) deviennent des livraisons vers les endpoints des restaurants. Aucun module n'en référence un
// autre : seul le host les relie.

/// <summary>
/// Integrations -> Tenants et Billing : écritures métier acceptées (inconnu = non) ; clés d'API incluses dans
/// l'abonnement (droit <c>storefront.api</c>). Fail-closed : un tenant autre que celui du contexte n'a droit à rien.
/// </summary>
internal sealed class IntegrationTenantsFromTenants(
    CachedTenants tenants, ITenantContext tenantContext, IQueryHandler<Billing.Application.Queries.GetFeatureLimitQuery, Billing.Application.Queries.FeatureLimitDto> limits)
    : IIntegrationTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<bool> AllowsApiKeysAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId == tenantId
        && (await limits.HandleAsync(new Billing.Application.Queries.GetFeatureLimitQuery(Billing.Application.BillingFeatures.StorefrontApi), cancellationToken)).Enabled;
}

/// <summary>
/// Publie les événements des autres modules aux webhooks, par l'outbox (après commit, au moins une fois), un scope DI par
/// message pour le tenant DU MESSAGE (<see cref="BackgroundTenant"/>). L'id de l'événement public est dérivé du module
/// et de l'id du message : un message rejoué donne le même id, donc aucune livraison de plus. Les données publiées
/// sont une LISTE BLANCHE : ids, montants, statuts, dates, quantités. Jamais un nom, un contact, un client, un motif
/// saisi librement, une référence de prestataire. Un refus métier part en file morte ; les autres erreurs sont
/// retentées.
/// </summary>
internal sealed class WebhookFeed(IServiceScopeFactory scopes, ILogger<WebhookFeed> logger) : IOutboxConsumer
{
    private static readonly Dictionary<(string Module, string Type), string> Published = new()
    {
        [("orders", "OrderPlaced")] = WebhookEvents.OrderPlaced,
        [("orders", "OrderCompleted")] = WebhookEvents.OrderCompleted,
        [("orders", "OrderCancelled")] = WebhookEvents.OrderCancelled,
        [("payments", "PaymentCaptured")] = WebhookEvents.PaymentCaptured,
        [("payments", "RefundSucceeded")] = WebhookEvents.RefundSucceeded,
        [("reservations", "ReservationCreated")] = WebhookEvents.ReservationCreated,
        [("reservations", "ReservationConfirmed")] = WebhookEvents.ReservationConfirmed,
        [("reservations", "ReservationCancelled")] = WebhookEvents.ReservationCancelled,
        [("reservations", "ReservationNoShow")] = WebhookEvents.ReservationNoShow,
        [("kitchen", "KitchenTicketReady")] = WebhookEvents.KitchenTicketReady,
        [("inventory", "LowStockReached")] = WebhookEvents.LowStockReached,
        [("catalog", "CatalogChanged")] = WebhookEvents.CatalogChanged,
    };

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Published.TryGetValue((message.Module, message.Type), out var eventType))
        {
            return;
        }

        var (tenantId, data) = Data(message);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
        if (!await services.GetRequiredService<IPlanFeatureGate>().AllowsAsync(TenantId.From(tenantId), PlanFeatures.IntegrationsWebhooks, cancellationToken))
        {
            // Option webhooks perdue : rien n'est livré, sans erreur.
            logger.LogInformation("Outbox message {Module}/{MessageId} not published: webhooks not included in the plan.", message.Module, message.Id);
            return;
        }

        var created = await services.GetRequiredService<ICommandHandler<PublishWebhookEventCommand, int>>().HandleAsync(
            new PublishWebhookEventCommand(eventType, EventIdOf(message), message.OccurredAtUtc, data.ToJsonString()),
            cancellationToken);
        if (created > 0)
        {
            logger.LogInformation("Outbox message {Module}/{MessageId} published to {Count} webhook endpoints.", message.Module, message.Id, created);
        }
    }

    /// <summary>Id stable de l'événement public : SHA-256 de « module:id du message », mis en forme d'UUID (version 8).</summary>
    internal static Guid EventIdOf(OutboxEnvelope message)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{message.Module}:{message.Id.ToString(CultureInfo.InvariantCulture)}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>Les données publiques de l'événement (liste blanche, noms en snake_case).</summary>
    internal static (Guid TenantId, JsonObject Data) Data(OutboxEnvelope message) => message.Type switch
    {
        "OrderPlaced" => Placed(message.Read<OrdersContracts.OrderPlaced>()),
        "OrderCompleted" => Completed(message.Read<OrdersContracts.OrderCompleted>()),
        "OrderCancelled" => Cancelled(message.Read<OrdersContracts.OrderCancelled>()),
        "PaymentCaptured" => Captured(message.Read<PaymentsContracts.PaymentCaptured>()),
        "RefundSucceeded" => Refunded(message.Read<PaymentsContracts.RefundSucceeded>()),
        "ReservationCreated" => Created(message.Read<ReservationsContracts.ReservationCreated>()),
        "ReservationConfirmed" => Reservation(message.Read<ReservationsContracts.ReservationConfirmed>()),
        "ReservationNoShow" => Reservation(message.Read<ReservationsContracts.ReservationNoShow>()),
        "ReservationCancelled" => ReservationCancelled(message.Read<ReservationsContracts.ReservationCancelled>()),
        "KitchenTicketReady" => TicketReady(message.Read<KitchenContracts.KitchenTicketReady>()),
        "LowStockReached" => LowStock(message.Read<InventoryContracts.LowStockReached>()),
        "CatalogChanged" => (message.Read<CatalogContracts.CatalogChanged>().TenantId, new JsonObject()),
        _ => throw new InvalidOperationException($"No webhook data for event type '{message.Type}'."),
    };

    private static (Guid, JsonObject) Placed(OrdersContracts.OrderPlaced placed) => (placed.TenantId, new JsonObject
    {
        ["order_id"] = Id(placed.OrderId),
        ["location_id"] = Id(placed.LocationId),
        ["order_number"] = placed.OrderNumber,
        ["order_type"] = placed.OrderType,
        ["sub_total"] = Money(placed.SubTotal),
        ["discount_total"] = Money(placed.DiscountTotal),
        ["tax_total"] = Money(placed.TaxTotal),
        ["total"] = Money(placed.Total),
    });

    private static (Guid, JsonObject) Completed(OrdersContracts.OrderCompleted completed) => (completed.TenantId, new JsonObject
    {
        ["order_id"] = Id(completed.OrderId),
        ["location_id"] = Id(completed.LocationId),
        ["total"] = Money(completed.Total),
    });

    private static (Guid, JsonObject) Cancelled(OrdersContracts.OrderCancelled cancelled) => (cancelled.TenantId, new JsonObject
    {
        ["order_id"] = Id(cancelled.OrderId),
        ["location_id"] = Id(cancelled.LocationId),
        ["previous_status"] = cancelled.PreviousStatus,
        ["total"] = Money(cancelled.Total),
    });

    private static (Guid, JsonObject) Captured(PaymentsContracts.PaymentCaptured captured) => (captured.TenantId, new JsonObject
    {
        ["payment_id"] = Id(captured.PaymentId),
        ["order_id"] = Id(captured.OrderId),
        ["amount"] = Money(captured.CapturedAmount),
    });

    private static (Guid, JsonObject) Refunded(PaymentsContracts.RefundSucceeded refunded) => (refunded.TenantId, new JsonObject
    {
        ["payment_id"] = Id(refunded.PaymentId),
        ["order_id"] = Id(refunded.OrderId),
        ["refund_id"] = Id(refunded.RefundId),
        ["amount"] = Money(refunded.Amount),
        ["total_refunded"] = Money(refunded.TotalRefunded),
        ["is_full_refund"] = refunded.IsFullRefund,
    });

    private static (Guid, JsonObject) Created(ReservationsContracts.ReservationCreated created) => (created.TenantId, new JsonObject
    {
        ["reservation_id"] = Id(created.ReservationId),
        ["location_id"] = Id(created.LocationId),
        ["party_size"] = created.PartySize,
        ["start_at_utc"] = created.StartAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        ["duration_minutes"] = (int)created.Duration.TotalMinutes,
    });

    private static (Guid, JsonObject) Reservation(ReservationsContracts.ReservationConfirmed confirmed) => (confirmed.TenantId, new JsonObject
    {
        ["reservation_id"] = Id(confirmed.ReservationId),
        ["location_id"] = Id(confirmed.LocationId),
    });

    private static (Guid, JsonObject) Reservation(ReservationsContracts.ReservationNoShow noShow) => (noShow.TenantId, new JsonObject
    {
        ["reservation_id"] = Id(noShow.ReservationId),
        ["location_id"] = Id(noShow.LocationId),
    });

    private static (Guid, JsonObject) ReservationCancelled(ReservationsContracts.ReservationCancelled cancelled) => (cancelled.TenantId, new JsonObject
    {
        ["reservation_id"] = Id(cancelled.ReservationId),
        ["location_id"] = Id(cancelled.LocationId),
        ["cancelled_by"] = cancelled.CancelledBy,
    });

    private static (Guid, JsonObject) TicketReady(KitchenContracts.KitchenTicketReady ready) => (ready.TenantId, new JsonObject
    {
        ["ticket_id"] = Id(ready.TicketId),
        ["order_id"] = Id(ready.OrderId),
        ["location_id"] = Id(ready.LocationId),
        ["ready_item_count"] = ready.ReadyItemCount,
        ["total_item_count"] = ready.TotalItemCount,
    });

    private static (Guid, JsonObject) LowStock(InventoryContracts.LowStockReached low) => (low.TenantId, new JsonObject
    {
        ["inventory_item_id"] = Id(low.InventoryItemId),
        ["location_id"] = Id(low.LocationId),
        ["ingredient_id"] = Id(low.IngredientId),
        ["current_quantity"] = low.CurrentQuantity,
        ["reorder_threshold"] = low.ReorderThreshold,
        ["unit"] = low.Unit,
    });

    private static string Id(Guid id) => id.ToString("D");

    private static JsonObject Money(MoneyAmount money) => new()
    {
        ["amount"] = money.Amount,
        ["currency"] = money.Currency,
    };
}

/// <summary>
/// Un endpoint désactivé par le système (trop de livraisons abandonnées d'affilée) : chaque membre ACTIF du personnel qui
/// a <c>integrations.manage</c> reçoit une notification in-app (URL masquée, nombre d'échecs). Une fois par membre et par
/// désactivation (clé d'idempotence). Un refus métier part en file morte ; les autres erreurs sont retentées.
/// </summary>
internal sealed class WebhookHealthFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("integrations", "WebhookEndpointAutoDisabled"))
        {
            return;
        }

        var disabled = message.Read<SaasRestaurant.Contracts.Integrations.WebhookEndpointAutoDisabled>();
        var tenantId = TenantId.From(disabled.TenantId);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenantId.Value), cancellationToken);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["restaurant.name"] = restaurant.Name,
            ["webhook.url"] = disabled.RedactedUrl,
            ["webhook.failures"] = disabled.ConsecutiveDeadLetters.ToString(CultureInfo.InvariantCulture),
        };

        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        foreach (var userId in await ManagersAsync(services, tenantId, cancellationToken))
        {
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    NotificationTypes.StaffWebhookDisabled.Key.Value, NotificationChannel.InApp, UserId: userId, Variables: variables,
                    IdempotencyKey: $"integrations:{message.Id.ToString(CultureInfo.InvariantCulture)}:{userId:N}"),
                cancellationToken);
        }
    }

    /// <summary>Les utilisateurs actifs du restaurant qui peuvent gérer les intégrations (Staff : rôles, sans cache).</summary>
    private static async Task<List<Guid>> ManagersAsync(IServiceProvider services, TenantId tenantId, CancellationToken cancellationToken)
    {
        var list = services.GetRequiredService<IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>>>();
        var permissions = services.GetRequiredService<IPermissionChecker>();
        var managers = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var members = await list.HandleAsync(new ListStaffQuery(page, ListStaffQuery.MaxPageSize), cancellationToken);
            foreach (var member in members.Items.Where(member => member.Status == StaffStatus.Active))
            {
                if (await permissions.HasTenantPermissionAsync(UserId.From(member.UserId), tenantId, IntegrationsPermissions.Manage, cancellationToken))
                {
                    managers.Add(member.UserId);
                }
            }

            if (!members.HasMore || members.Items.Count == 0)
            {
                return managers;
            }
        }
    }
}

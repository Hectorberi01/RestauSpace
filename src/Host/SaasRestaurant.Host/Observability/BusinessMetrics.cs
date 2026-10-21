using System.Diagnostics.Metrics;
using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.Contracts;

namespace SaasRestaurant.Host;

internal sealed class BusinessMetrics
{
    public const string Name = "SaasRestaurant.Business";

    private static readonly Meter Meter = new(Name);

    private static readonly Counter<long> OrdersPlaced = Meter.CreateCounter<long>("saasrestaurant.orders.placed", "{order}");
    private static readonly Counter<long> OrdersCompleted = Meter.CreateCounter<long>("saasrestaurant.orders.completed", "{order}");
    private static readonly Counter<long> OrdersCancelled = Meter.CreateCounter<long>("saasrestaurant.orders.cancelled", "{order}");
    private static readonly Counter<double> Sales = Meter.CreateCounter<double>("saasrestaurant.sales.amount", null, "Total of completed orders, per currency.");
    private static readonly Counter<long> PaymentsCaptured = Meter.CreateCounter<long>("saasrestaurant.payments.captured", "{payment}");
    private static readonly Counter<double> CapturedAmount = Meter.CreateCounter<double>("saasrestaurant.payments.captured.amount", null, "Amount captured, per currency.");
    private static readonly Counter<long> PaymentsFailed = Meter.CreateCounter<long>("saasrestaurant.payments.failed", "{payment}");
    private static readonly Counter<long> Refunds = Meter.CreateCounter<long>("saasrestaurant.refunds.succeeded", "{refund}");
    private static readonly Counter<double> RefundedAmount = Meter.CreateCounter<double>("saasrestaurant.refunds.amount", null, "Amount refunded, per currency.");
    private static readonly Counter<long> SubscriptionEvents = Meter.CreateCounter<long>("saasrestaurant.subscriptions.events", "{event}");
    private static readonly Counter<long> TenantEvents = Meter.CreateCounter<long>("saasrestaurant.tenants.events", "{event}");
    private static readonly Counter<long> NotificationEvents = Meter.CreateCounter<long>("saasrestaurant.notifications.events", "{event}");

    private PlatformSnapshot _snapshot = PlatformSnapshot.Empty;

    public BusinessMetrics()
    {
        Meter.CreateObservableGauge<long>("saasrestaurant.tenants", () => Volatile.Read(ref _snapshot).Tenants, "{tenant}", "Restaurants by status.");
        Meter.CreateObservableGauge<long>("saasrestaurant.subscriptions", () => Volatile.Read(ref _snapshot).Subscriptions, "{subscription}", "Subscriptions by status.");
        Meter.CreateObservableGauge<double>("saasrestaurant.mrr", () => Volatile.Read(ref _snapshot).MonthlyRecurringRevenue, null, "Monthly recurring revenue, per currency.");
        Meter.CreateObservableGauge<long>("saasrestaurant.outbox.pending", () => Volatile.Read(ref _snapshot).OutboxPending, "{message}", "Outbox messages waiting for dispatch.");
        Meter.CreateObservableGauge<double>("saasrestaurant.outbox.oldest_pending.age", () => Volatile.Read(ref _snapshot).OutboxOldestAge, "s", "Age of the oldest pending outbox message.");
        Meter.CreateObservableGauge<long>("saasrestaurant.outbox.dead_letters", () => Volatile.Read(ref _snapshot).OutboxDeadLetters, "{message}", "Outbox messages in the dead letter queue.");
        Meter.CreateObservableGauge<double>("saasrestaurant.platform.snapshot.age", SnapshotAge, "s", "Age of the last platform snapshot.");
    }

    public void Publish(PlatformSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);

    private Measurement<double> SnapshotAge()
    {
        var takenAt = Volatile.Read(ref _snapshot).TakenAtUtc;
        return new Measurement<double>(takenAt is { } at ? (DateTimeOffset.UtcNow - at).TotalSeconds : -1);
    }

    public static void OrderPlaced(Guid tenantId, string orderType) =>
        OrdersPlaced.Add(1, Tenant(tenantId), new KeyValuePair<string, object?>("order_type", orderType));

    public static void OrderCompleted(Guid tenantId, MoneyAmount total)
    {
        OrdersCompleted.Add(1, Tenant(tenantId), CurrencyOf(total));
        Sales.Add((double)total.Amount, Tenant(tenantId), CurrencyOf(total));
    }

    public static void OrderCancelled(Guid tenantId, string previousStatus) =>
        OrdersCancelled.Add(1, Tenant(tenantId), new KeyValuePair<string, object?>("previous_status", previousStatus));

    public static void PaymentCaptured(Guid tenantId, MoneyAmount amount)
    {
        PaymentsCaptured.Add(1, Tenant(tenantId), CurrencyOf(amount));
        CapturedAmount.Add((double)amount.Amount, Tenant(tenantId), CurrencyOf(amount));
    }

    public static void PaymentFailed(Guid tenantId) => PaymentsFailed.Add(1, Tenant(tenantId));

    public static void RefundSucceeded(Guid tenantId, MoneyAmount amount)
    {
        Refunds.Add(1, Tenant(tenantId), CurrencyOf(amount));
        RefundedAmount.Add((double)amount.Amount, Tenant(tenantId), CurrencyOf(amount));
    }

    public static void SubscriptionEvent(string type) => SubscriptionEvents.Add(1, Event(type));

    public static void TenantEvent(string type) => TenantEvents.Add(1, Event(type));

    public static void NotificationEvent(string type) => NotificationEvents.Add(1, Event(type));

    private static KeyValuePair<string, object?> Tenant(Guid tenantId) => new("tenant_id", tenantId.ToString());

    private static KeyValuePair<string, object?> CurrencyOf(MoneyAmount amount) => new("currency", amount.Currency);

    private static KeyValuePair<string, object?> Event(string type) => new("event", type);
}

internal sealed record PlatformSnapshot(
    DateTimeOffset? TakenAtUtc,
    IReadOnlyList<Measurement<long>> Tenants,
    IReadOnlyList<Measurement<long>> Subscriptions,
    IReadOnlyList<Measurement<double>> MonthlyRecurringRevenue,
    IReadOnlyList<Measurement<long>> OutboxPending,
    IReadOnlyList<Measurement<double>> OutboxOldestAge,
    IReadOnlyList<Measurement<long>> OutboxDeadLetters)
{
    public static readonly PlatformSnapshot Empty = new(null, [], [], [], [], [], []);
}

internal sealed class BusinessMetricsFlow : IOutboxConsumer
{
    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Module, message.Type)
        {
            case ("orders", "OrderPlaced"):
                var placed = message.Read<SaasRestaurant.Contracts.Orders.OrderPlaced>();
                BusinessMetrics.OrderPlaced(placed.TenantId, placed.OrderType);
                break;
            case ("orders", "OrderCompleted"):
                var completed = message.Read<SaasRestaurant.Contracts.Orders.OrderCompleted>();
                BusinessMetrics.OrderCompleted(completed.TenantId, completed.Total);
                break;
            case ("orders", "OrderCancelled"):
                var cancelled = message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>();
                BusinessMetrics.OrderCancelled(cancelled.TenantId, cancelled.PreviousStatus);
                break;
            case ("payments", "PaymentCaptured"):
                var captured = message.Read<SaasRestaurant.Contracts.Payments.PaymentCaptured>();
                BusinessMetrics.PaymentCaptured(captured.TenantId, captured.CapturedAmount);
                break;
            case ("payments", "PaymentFailed"):
                BusinessMetrics.PaymentFailed(message.Read<SaasRestaurant.Contracts.Payments.PaymentFailed>().TenantId);
                break;
            case ("payments", "RefundSucceeded"):
                var refunded = message.Read<SaasRestaurant.Contracts.Payments.RefundSucceeded>();
                BusinessMetrics.RefundSucceeded(refunded.TenantId, refunded.Amount);
                break;
            case ("billing", var type) when type.StartsWith("Subscription", StringComparison.Ordinal):
                BusinessMetrics.SubscriptionEvent(type);
                break;
            case ("tenants", var type) when type.StartsWith("Tenant", StringComparison.Ordinal):
                BusinessMetrics.TenantEvent(type);
                break;
            case ("notifications", var type):
                BusinessMetrics.NotificationEvent(type);
                break;
        }

        return Task.CompletedTask;
    }
}

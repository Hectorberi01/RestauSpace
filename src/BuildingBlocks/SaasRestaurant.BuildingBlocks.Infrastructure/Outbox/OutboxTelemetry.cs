using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public static class OutboxTelemetry
{
    public const string Name = "SaasRestaurant.Outbox";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    private static readonly Counter<long> Deliveries =
        Meter.CreateCounter<long>("saasrestaurant.outbox.deliveries", "{delivery}", "Deliveries of outbox messages to consumers, by outcome.");

    private static readonly Counter<long> DeadLettered =
        Meter.CreateCounter<long>("saasrestaurant.outbox.dead_lettered", "{message}", "Outbox messages moved to the dead letter queue.");

    private static readonly Histogram<double> ConsumerDuration =
        Meter.CreateHistogram<double>("saasrestaurant.outbox.consumer.duration", "s", "Time spent by a consumer on one message.");

    private static readonly Histogram<double> Lag =
        Meter.CreateHistogram<double>("saasrestaurant.outbox.lag", "s", "Delay between an event and its first dispatch.");

    public static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: { Length: <= OutboxMessage.MaxTraceParentLength } id } ? id : null;

    public static Activity? StartConsumer(string module, OutboxMessage message, string consumer)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = ActivityContext.TryParse(message.TraceParent, null, out var parent);
        var activity = Source.StartActivity($"outbox {module}.{message.Type}", ActivityKind.Consumer, parent);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("messaging.system", "outbox");
        activity.SetTag("messaging.destination.name", module);
        activity.SetTag("messaging.message.id", message.Id);
        activity.SetTag("messaging.operation.type", "process");
        activity.SetTag("saasrestaurant.event.type", message.Type);
        activity.SetTag("saasrestaurant.outbox.consumer", ShortName(consumer));
        activity.SetTag("saasrestaurant.outbox.attempt", message.AttemptCount + 1);
        return activity;
    }

    public static void Delivered(string module, string consumer, string outcome, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "module", module },
            { "consumer", ShortName(consumer) },
            { "outcome", outcome },
        };
        Deliveries.Add(1, tags);
        ConsumerDuration.Record(elapsed.TotalSeconds, tags);
    }

    public static void FirstDispatch(string module, string type, TimeSpan lag) =>
        Lag.Record(Math.Max(0, lag.TotalSeconds), new TagList { { "module", module }, { "event_type", type } });

    public static void MovedToDeadLetter(string module, string type) =>
        DeadLettered.Add(1, new TagList { { "module", module }, { "event_type", type } });

    public static string ShortName(string consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var dot = consumer.LastIndexOf('.');
        return dot < 0 ? consumer : consumer[(dot + 1)..];
    }
}

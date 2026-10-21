using System.Diagnostics;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public sealed class OutboxTelemetryTests
{
    private sealed record SomethingHappened(DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

    private static readonly ActivitySource Source = new("SaasRestaurant.Tests.Outbox");

    [Fact]
    public void A_message_keeps_the_trace_of_the_request_that_raised_it_and_its_consumer_continues_it()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "SaasRestaurant.Tests.Outbox" or OutboxTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        OutboxMessage message;
        ActivityTraceId requestTrace;
        using (var request = Source.StartActivity("POST /v1/orders"))
        {
            requestTrace = request!.TraceId;
            message = OutboxMessage.From(new SomethingHappened(DateTimeOffset.UtcNow));
        }

        using var consumer = OutboxTelemetry.StartConsumer("orders", message, "SaasRestaurant.Host.SomeFlow");

        Assert.NotNull(message.TraceParent);
        Assert.Equal(requestTrace, consumer!.TraceId);
        Assert.Equal(ActivityKind.Consumer, consumer.Kind);
        Assert.Equal("SomeFlow", consumer.GetTagItem("saasrestaurant.outbox.consumer"));
    }
}

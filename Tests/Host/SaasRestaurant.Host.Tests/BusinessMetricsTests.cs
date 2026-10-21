using System.Diagnostics.Metrics;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host.Tests;

public sealed class BusinessMetricsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static async Task<List<(string Instrument, double Value, Dictionary<string, object?> Tags)>> CaptureAsync(Guid tenant, Func<Task> act)
    {
        var captured = new List<(string, double, Dictionary<string, object?>)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == BusinessMetrics.Name)
            {
                owner.EnableMeasurementEvents(instrument);
            }
        };
        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            if (Equals(copy.GetValueOrDefault("tenant_id"), tenant.ToString()))
            {
                lock (captured)
                {
                    captured.Add((instrument.Name, value, copy));
                }
            }
        }

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
        await act();
        return captured;
    }

    private static string Completed(Guid tenant) =>
        $$"""{"TenantId":"{{tenant}}","OrderId":"{{Guid.NewGuid()}}","LocationId":"{{Guid.NewGuid()}}","Total":{"Amount":4500,"Currency":"XOF"},"CustomerId":null}""";

    [Fact]
    public async Task A_completed_order_counts_once_with_its_amount_tenant_and_currency()
    {
        var tenant = Guid.NewGuid();
        var flow = new BusinessMetricsFlow();

        var captured = await CaptureAsync(tenant, () => flow.HandleAsync(new OutboxEnvelope("orders", 1, "OrderCompleted", Completed(tenant), At)));

        Assert.Equal(1d, Assert.Single(captured, entry => entry.Instrument == "saasrestaurant.orders.completed").Value);
        var sales = Assert.Single(captured, entry => entry.Instrument == "saasrestaurant.sales.amount");
        Assert.Equal((4500d, (object?)"XOF"), (sales.Value, sales.Tags["currency"]));
    }

    [Fact]
    public async Task The_same_event_from_another_module_is_ignored()
    {
        var tenant = Guid.NewGuid();
        var flow = new BusinessMetricsFlow();

        var captured = await CaptureAsync(tenant, () => flow.HandleAsync(new OutboxEnvelope("kitchen", 2, "OrderCompleted", Completed(tenant), At)));

        Assert.Empty(captured);
    }
}

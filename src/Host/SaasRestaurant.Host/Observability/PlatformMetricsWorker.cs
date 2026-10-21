using System.Diagnostics.Metrics;
using Reporting.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

internal sealed record PlatformMetricsSettings(TimeSpan Interval);

internal sealed class PlatformMetricsWorker(
    IServiceScopeFactory scopes,
    IEnumerable<IOutboxMonitor> monitors,
    BusinessMetrics metrics,
    PlatformMetricsSettings settings,
    IClock clock,
    ILogger<PlatformMetricsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Interval);
        do
        {
            try
            {
                metrics.Publish(await SnapshotAsync(stoppingToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Platform metrics snapshot failed; previous values are kept.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<PlatformSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var pending = new List<Measurement<long>>();
        var oldest = new List<Measurement<double>>();
        var deadLetters = new List<Measurement<long>>();
        foreach (var monitor in monitors)
        {
            var status = await monitor.GetStatusAsync(cancellationToken);
            var module = new KeyValuePair<string, object?>("module", status.Module);
            pending.Add(new Measurement<long>(status.Pending, module));
            deadLetters.Add(new Measurement<long>(status.DeadLettered, module));
            oldest.Add(new Measurement<double>(status.OldestPendingAtUtc is { } at ? Math.Max(0, (now - at).TotalSeconds) : 0, module));
        }

        await using var scope = scopes.CreateAsyncScope();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var dashboard = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetPlatformDashboardQuery, PlatformDashboardDto>>()
            .HandleAsync(new GetPlatformDashboardQuery(today, today), cancellationToken);

        return new PlatformSnapshot(
            now,
            dashboard.TenantsByStatus.Select(row => new Measurement<long>(row.Count, new KeyValuePair<string, object?>("status", row.Status.ToString()))).ToList(),
            dashboard.SubscriptionsByStatus.Select(row => new Measurement<long>(row.Count, new KeyValuePair<string, object?>("status", row.Status.ToString()))).ToList(),
            dashboard.MonthlyRecurringRevenue.Select(row => new Measurement<double>((double)row.Amount, new KeyValuePair<string, object?>("currency", row.CurrencyCode))).ToList(),
            pending,
            oldest,
            deadLetters);
    }
}

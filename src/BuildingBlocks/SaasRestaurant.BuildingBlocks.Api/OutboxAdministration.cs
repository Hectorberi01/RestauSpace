using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SaasRestaurant.BuildingBlocks.Api;

public static class OutboxAdministration
{
    public static readonly DomainError ModuleNotFound = new("outbox.module.not_found", "No outbox is known for this module.");
    public static readonly DomainError MessageNotFound = new("outbox.message.not_found", "The message is not in the dead-letter list.");

    public static IServiceCollection AddOutboxMonitoring(this IServiceCollection services, TimeSpan? maxPendingAge = null)
    {
        services.AddSingleton(new OutboxHealthSettings(maxPendingAge ?? TimeSpan.FromMinutes(5)));
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox", HealthStatus.Degraded);
        return services;
    }

    public static RouteGroupBuilder MapOutboxAdministration(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin/outbox").WithTags("Outbox");

        group.MapGet("/", async (IEnumerable<IOutboxMonitor> monitors, CancellationToken ct) =>
        {
            var statuses = new List<OutboxStatus>();
            foreach (var monitor in monitors.OrderBy(candidate => candidate.Module, StringComparer.Ordinal))
            {
                statuses.Add(await monitor.GetStatusAsync(ct));
            }

            return TypedResults.Ok(statuses);
        });

        group.MapGet("/{module}/dead-letters", async (
            string module, int? page, int? pageSize, IEnumerable<IOutboxMonitor> monitors, CancellationToken ct) =>
            TypedResults.Ok(await Find(monitors, module).ListDeadLettersAsync(page ?? 1, pageSize ?? 50, ct)));

        group.MapPost("/{module}/dead-letters/{messageId:long}/replay", async (
            string module, long messageId, IEnumerable<IOutboxMonitor> monitors, CancellationToken ct) =>
        {
            if (!await Find(monitors, module).ReplayAsync(messageId, ct))
            {
                throw new NotFoundException(MessageNotFound);
            }

            return TypedResults.NoContent();
        });

        return group;
    }

    private static IOutboxMonitor Find(IEnumerable<IOutboxMonitor> monitors, string module) =>
        monitors.FirstOrDefault(candidate => string.Equals(candidate.Module, module, StringComparison.Ordinal))
        ?? throw new NotFoundException(ModuleNotFound);
}

public sealed record OutboxHealthSettings(TimeSpan MaxPendingAge);

public sealed class OutboxHealthCheck(IEnumerable<IOutboxMonitor> monitors, OutboxHealthSettings settings, IClock clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var monitor in monitors)
        {
            var status = await monitor.GetStatusAsync(cancellationToken);
            data[status.Module] = new { status.Pending, status.OldestPendingAtUtc, status.DeadLettered };
            if (status.DeadLettered > 0)
            {
                problems.Add($"{status.Module}: {status.DeadLettered} dead letter(s)");
            }

            if (status.OldestPendingAtUtc is { } oldest && clock.UtcNow - oldest > settings.MaxPendingAge)
            {
                problems.Add($"{status.Module}: pending since {oldest:O}");
            }
        }

        return problems.Count == 0
            ? HealthCheckResult.Healthy(data: data)
            : new HealthCheckResult(context.Registration.FailureStatus, string.Join("; ", problems), data: data);
    }
}

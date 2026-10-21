using Audit.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Audit.Infrastructure.Workers;

/// <summary>
/// Rétention : les écritures plus anciennes que <see cref="AuditOptions.Retention"/> sont purgées par lots (seule
/// suppression autorisée en base). Rien si la rétention est nulle. Journal : compteurs seulement.
/// </summary>
public sealed class AuditRetentionWorker(IServiceScopeFactory scopes, AuditOptions options, IClock clock, ILogger<AuditRetentionWorker> logger)
    : BackgroundService
{
    /// <summary>Un passage complet (lots successifs) ; renvoie le nombre d'écritures purgées.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        if (options.Retention <= TimeSpan.Zero)
        {
            return 0;
        }

        var cutoff = clock.UtcNow - options.Retention;
        var total = 0;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var purged = await AuditDatabase.PurgeAsync(
                scope.ServiceProvider.GetRequiredService<AuditDbContext>(), cutoff, options.PurgeBatchSize, cancellationToken);
            total += purged;
            if (purged < options.PurgeBatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            logger.LogInformation("{Count} audit entries older than the retention period purged.", total);
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Audit retention pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(options.PurgeInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

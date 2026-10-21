using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reporting.Application;
using Reporting.Application.Abstractions;
using Reporting.Application.Commands;

namespace Reporting.Infrastructure.Workers;

/// <summary>
/// Le job du module, à chaque passage : (1) les rapports planifiés arrivés à échéance mettent un export en file ; (2) les
/// exports en file sont produits ; (3) les exports terminés depuis plus que la durée de conservation expirent. Chaque
/// élément est traité dans son propre scope POUR son tenant (<see cref="BackgroundTenant"/>). Plusieurs instances
/// peuvent tourner : un conflit de version remet l'élément au passage suivant. Journal : ids, compteurs et codes.
/// </summary>
public sealed class ReportingWorker(
    IServiceScopeFactory scopes, ReportingOptions options, ReportingSettings settings, IClock clock, ILogger<ReportingWorker> logger) : BackgroundService
{
    /// <summary>Un passage complet ; renvoie le nombre d'éléments dont l'état a changé.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        IReadOnlyList<TenantWork> deliveries;
        await using (var scope = scopes.CreateAsyncScope())
        {
            deliveries = await scope.ServiceProvider.GetRequiredService<IReportingWorkQueue>().ListDueDeliveriesAsync(now, options.BatchSize, cancellationToken);
        }

        var changed = 0;
        foreach (var work in deliveries)
        {
            changed += await RunAsync(work, new RunScheduledReportCommand(work.Id), (ScheduledRunOutcome outcome) => outcome != ScheduledRunOutcome.Nothing, cancellationToken);
        }

        changed += await ProcessExportsAsync(cancellationToken);

        IReadOnlyList<TenantWork> expired;
        await using (var scope = scopes.CreateAsyncScope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<IReportingWorkQueue>()
                .ListExpiredExportsAsync(clock.UtcNow - settings.ExportRetention, options.BatchSize, cancellationToken);
        }

        foreach (var work in expired)
        {
            changed += await RunAsync(work, new ExpireExportCommand(work.Id), (bool done) => done, cancellationToken);
        }

        return changed;
    }

    /// <summary>Produit les exports en file (aussi appelé seul par les tests).</summary>
    public async Task<int> ProcessExportsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantWork> queued;
        await using (var scope = scopes.CreateAsyncScope())
        {
            queued = await scope.ServiceProvider.GetRequiredService<IReportingWorkQueue>().ListQueuedExportsAsync(options.BatchSize, cancellationToken);
        }

        var changed = 0;
        foreach (var work in queued)
        {
            changed += await RunAsync(work, new GenerateExportCommand(work.Id), (ExportOutcome outcome) => outcome != ExportOutcome.Nothing, cancellationToken);
        }

        return changed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reporting pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(options.WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> RunAsync<TCommand, TResult>(
        TenantWork work, TCommand command, Func<TResult, bool> isChange, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(work.TenantId);
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().HandleAsync(command, cancellationToken);
            if (!isChange(result))
            {
                return 0;
            }

            logger.LogInformation("{Command} {Id} of tenant {TenantId}: {Result}.", typeof(TCommand).Name, work.Id, work.TenantId.Value, result);
            return 1;
        }
        catch (Exception exception) when (exception is ConcurrencyException or DomainException or NotFoundException)
        {
            logger.LogWarning("{Command} {Id} postponed: {Error}.", typeof(TCommand).Name, work.Id, exception.GetType().Name);
            return 0;
        }
    }
}

using Backups.Application.Commands;
using Backups.Application.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backups.Infrastructure.Workers;

/// <summary>Crée la sauvegarde planifiée du jour (Backups:Schedule). Journal : ids et compteurs seulement.</summary>
public sealed class BackupScheduleWorker(IServiceScopeFactory scopes, BackupsOptions options, ILogger<BackupScheduleWorker> logger)
    : BackgroundService
{
    public async Task<BackupDto?> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var requested = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunScheduledBackupCommand, BackupDto?>>()
            .HandleAsync(new RunScheduledBackupCommand(), cancellationToken);
        if (requested is not null)
        {
            logger.LogInformation("Scheduled backup {BackupId} requested.", requested.Id);
        }

        return requested;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        BackupLoop.RunAsync(() => RunOnceAsync(stoppingToken), options.ScheduleInterval, logger, "Backup schedule", stoppingToken);
}

/// <summary>Timeouts (heartbeat absent) et rétention. Journal : compteurs seulement.</summary>
public sealed class BackupMaintenanceWorker(IServiceScopeFactory scopes, BackupsOptions options, ILogger<BackupMaintenanceWorker> logger)
    : BackgroundService
{
    public async Task<MaintenanceReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunMaintenanceCommand, MaintenanceReport>>()
            .HandleAsync(new RunMaintenanceCommand(), cancellationToken);
        if (report.BackupsTimedOut + report.RestoresTimedOut + report.BackupsExpired > 0)
        {
            logger.LogInformation(
                "Backup maintenance: {BackupsTimedOut} backups and {RestoresTimedOut} restores timed out, {BackupsExpired} backups expired.",
                report.BackupsTimedOut, report.RestoresTimedOut, report.BackupsExpired);
        }

        return report;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        BackupLoop.RunAsync(() => RunOnceAsync(stoppingToken), options.MaintenanceInterval, logger, "Backup maintenance", stoppingToken);
}

internal static class BackupLoop
{
    public static async Task RunAsync(Func<Task> pass, TimeSpan interval, ILogger logger, string name, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await pass();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Le type seulement : un message d'exception peut contenir une donnée de connexion.
                logger.LogError("{Pass} pass failed ({ExceptionType}); retrying at the next interval.", name, exception.GetType().Name);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

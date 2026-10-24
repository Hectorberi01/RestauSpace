using Backups.Application;
using Backups.Application.Abstractions;
using Backups.Infrastructure.Persistence;
using Backups.Infrastructure.Storage;
using Backups.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Backups.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module, lu aussi par l'agent du serveur de base (contrat).</summary>
    public const string Schema = "backups";

    /// <summary>File des demandes, pré-signature (lecture seule), planification et maintenance. L'hôte fournit <see cref="IAdminTwoFactor"/>.</summary>
    public static IServiceCollection AddBackupsInfrastructure(
        this IServiceCollection services, string connectionString, Action<BackupsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var backupsOptions = new BackupsOptions();
        configure?.Invoke(backupsOptions);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(backupsOptions.ScheduleInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(backupsOptions.MaintenanceInterval, TimeSpan.Zero);
        services.TryAddSingleton(backupsOptions);
        services.TryAddSingleton(backupsOptions.Storage);

        services.AddDbContext<BackupsDbContext>(options => options.UseBackupsNpgsql(connectionString));
        services.AddScoped<IBackupRepository, BackupRepository>();
        services.AddScoped<IRestoreRepository, RestoreRepository>();
        services.AddScoped<IBackupAgentReader, BackupAgentReader>();
        services.AddScoped<IBackupsUnitOfWork, BackupsUnitOfWork>();
        services.TryAddSingleton<IBackupStorage, S3BackupStorage>();
        services.AddOutboxDispatcher<BackupsDbContext>();

        services.AddSingleton<BackupScheduleWorker>();
        services.AddSingleton<BackupMaintenanceWorker>();
        if (backupsOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<BackupScheduleWorker>());
            services.AddHostedService(provider => provider.GetRequiredService<BackupMaintenanceWorker>());
        }

        return services;
    }

    internal static DbContextOptionsBuilder UseBackupsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

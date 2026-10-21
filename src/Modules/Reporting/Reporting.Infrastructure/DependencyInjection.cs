using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reporting.Application;
using Reporting.Application.Abstractions;
using Reporting.Infrastructure.Persistence;
using Reporting.Infrastructure.Workers;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reporting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propriété du module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "reporting";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité), <c>IReportingTenants</c> et <c>IReportingCurrentUser</c> ; il peut remplacer le stockage des
    /// fichiers d'export (<see cref="IExportFileStore"/>, en base par défaut) par le module Files.
    /// </summary>
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services, string connectionString, Action<ReportingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var reportingOptions = new ReportingOptions();
        configure?.Invoke(reportingOptions);
        services.TryAddSingleton(reportingOptions);
        services.TryAddSingleton(new ReportingSettings());

        // Hors requête (job, consommateur d'outbox), le tenant du scope est celui fixé par le traitement.
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<ReportingDbContext>(options => options.UseReportingNpgsql(connectionString));
        services.AddScoped<IReportDefinitionRepository, ReportDefinitionRepository>();
        services.AddScoped<IExportJobRepository, ExportJobRepository>();
        services.AddScoped<IExportRequestStore, ExportRequestStore>();
        services.AddScoped<IReportDeliveryStore, ReportDeliveryStore>();
        services.AddScoped<IReportingWorkQueue, ReportingWorkQueue>();
        services.AddScoped<IMetricsStore, MetricsStore>();
        services.AddScoped<IPlatformDirectory, PlatformDirectory>();
        services.TryAddScoped<IExportFileStore, DatabaseExportFileStore>();
        services.AddScoped<IReportingUnitOfWork, ReportingUnitOfWork>();
        services.AddSingleton<ReportingWorker>();
        if (reportingOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<ReportingWorker>());
        }

        services.AddScoped<IOutboxConsumer, ExportFileCleanup>();
        services.AddOutboxDispatcher<ReportingDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseReportingNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

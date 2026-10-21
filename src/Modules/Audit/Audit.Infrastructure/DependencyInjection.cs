using Audit.Application.Abstractions;
using Audit.Infrastructure.Persistence;
using Audit.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Audit.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "audit";

    /// <summary>Enregistre la persistance du journal (ajout seul) et le job de rétention.</summary>
    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services, string connectionString, Action<AuditOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var auditOptions = new AuditOptions();
        configure?.Invoke(auditOptions);
        ArgumentOutOfRangeException.ThrowIfLessThan(auditOptions.Retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(auditOptions.PurgeBatchSize, 1);
        services.TryAddSingleton(auditOptions);

        services.AddDbContext<AuditDbContext>(options => options.UseAuditNpgsql(connectionString));
        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        services.AddScoped<IAuditUnitOfWork, AuditUnitOfWork>();

        services.AddSingleton<AuditRetentionWorker>();
        if (auditOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<AuditRetentionWorker>());
        }

        return services;
    }

    internal static DbContextOptionsBuilder UseAuditNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

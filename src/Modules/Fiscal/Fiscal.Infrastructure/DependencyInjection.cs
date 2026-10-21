using Fiscal.Application;
using Fiscal.Infrastructure.Emecf;
using Fiscal.Infrastructure.Persistence;
using Fiscal.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Fiscal.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propriété du module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "fiscal";

    /// <summary>
    /// Persistance, coffre des jetons, client e-MECeF et file de traitement. L'hôte fournit <see cref="ITenantContext"/>,
    /// <c>IFiscalTenants</c> et <c>IFiscalOrders</c>.
    /// </summary>
    public static IServiceCollection AddFiscalInfrastructure(this IServiceCollection services, string connectionString, Action<FiscalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new FiscalOptions();
        configure?.Invoke(options);
        if (options.TokenKey is not { Length: 32 })
        {
            throw new InvalidOperationException("Fiscal token key must be exactly 32 bytes (AES-256).");
        }

        services.AddSingleton(options);
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<FiscalDbContext>(builder => builder.UseFiscalNpgsql(connectionString));
        services.AddScoped<IFiscalProfileRepository, FiscalProfileRepository>();
        services.AddScoped<IFiscalInvoiceRepository, FiscalInvoiceRepository>();
        services.AddScoped<IDueFiscalInvoices, DueFiscalInvoices>();
        services.AddScoped<IFiscalTokenStore, FiscalTokenStore>();
        services.AddScoped<IFiscalUnitOfWork, FiscalUnitOfWork>();
        services.TryAddSingleton<IFiscalAuthority, EmecfClient>();
        services.AddOutboxDispatcher<FiscalDbContext>();
        services.AddSingleton<FiscalInvoiceWorker>();
        if (options.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<FiscalInvoiceWorker>());
        }

        return services;
    }

    internal static DbContextOptionsBuilder UseFiscalNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

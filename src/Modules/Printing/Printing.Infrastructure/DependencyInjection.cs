using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Printing.Application.Abstractions;
using Printing.Infrastructure.Persistence;

namespace Printing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propriété du module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "printing";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/>, <c>IPrintingTenants</c>,
    /// <c>IPrintingLocations</c> et <c>IPrinterClient</c>.
    /// </summary>
    public static IServiceCollection AddPrintingInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<PrintingDbContext>(options => options.UsePrintingNpgsql(connectionString));
        services.AddScoped<IPrinterRepository, PrinterRepository>();
        services.AddScoped<IPrinterTokenLookup, PrinterTokenLookup>();
        services.AddScoped<IPrintJobRepository, PrintJobRepository>();
        services.AddScoped<IPrintingUnitOfWork, PrintingUnitOfWork>();
        services.AddOutboxDispatcher<PrintingDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UsePrintingNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

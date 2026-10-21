using Customers.Application.Abstractions;
using Customers.Domain;
using Customers.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Customers.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ses tables ni ne fait de jointure
    /// dessus.</summary>
    public const string Schema = "customers";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité) et <c>ICustomersTenants</c> ; sans contexte de tenant, toute requête sur les clients ne renvoie
    /// rien.
    /// </summary>
    public static IServiceCollection AddCustomersInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<CustomersDbContext>(options => options.UseCustomersNpgsql(connectionString));
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IMarketingAudienceReader, MarketingAudienceReader>();
        services.AddScoped<ICustomerNoteRepository, CustomerNoteRepository>();
        services.AddScoped<ConsentRepository>();
        services.AddScoped<IConsentRepository>(provider => provider.GetRequiredService<ConsentRepository>());
        services.AddScoped<IConsentUniquenessChecker>(provider => provider.GetRequiredService<ConsentRepository>());
        services.AddScoped<ICustomerSettingsStore, CustomerSettingsStore>();
        services.AddScoped<ICustomersUnitOfWork, CustomersUnitOfWork>();
        services.AddOutboxDispatcher<CustomersDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseCustomersNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

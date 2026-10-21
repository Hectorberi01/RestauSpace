using Kitchen.Application.Abstractions;
using Kitchen.Domain;
using Kitchen.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Kitchen.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "kitchen";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité), <c>IKitchenTenants</c>, <c>IKitchenLocations</c>, <c>IKitchenProducts</c> et
    /// <c>IKitchenOrders</c> ; sans contexte de tenant, toute requête sur la cuisine ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddKitchenInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<KitchenDbContext>(options => options.UseKitchenNpgsql(connectionString));
        services.AddScoped<IKitchenStationRepository, KitchenStationRepository>();
        services.AddScoped<IStationRoutingRepository, StationRoutingRepository>();
        services.AddScoped<IKitchenTicketRepository, KitchenTicketRepository>();
        services.AddScoped<IKitchenStationNameUniquenessChecker, KitchenStationNameUniquenessChecker>();
        services.AddScoped<IStationRoutingUniquenessChecker, StationRoutingUniquenessChecker>();
        services.AddScoped<IKitchenUnitOfWork, KitchenUnitOfWork>();
        services.AddOutboxDispatcher<KitchenDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseKitchenNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

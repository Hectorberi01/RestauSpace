using Locations.Application.Abstractions;
using Locations.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Locations.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "locations";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête)
    /// et <c>ITenantDirectory</c> ; sans contexte de tenant, toute requête sur les établissements ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddLocationsInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<LocationsDbContext>(options => options.UseLocationsNpgsql(connectionString));
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IPublicLocationLookup, PublicLocationLookup>();
        services.AddScoped<ILocationsUnitOfWork, LocationsUnitOfWork>();
        services.AddOutboxDispatcher<LocationsDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseLocationsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

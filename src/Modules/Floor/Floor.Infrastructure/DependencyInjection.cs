using Floor.Application.Abstractions;
using Floor.Domain;
using Floor.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Floor.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "floor";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête),
    /// <c>IFloorTenants</c> et <c>IFloorLocations</c> ; sans contexte de tenant, toute requête sur les zones et les
    /// sessions ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddFloorInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<FloorDbContext>(options => options.UseFloorNpgsql(connectionString));
        services.AddScoped<IDiningAreaRepository, DiningAreaRepository>();
        services.AddScoped<ITableSessionRepository, TableSessionRepository>();
        services.AddScoped<IWaitingPartyRepository, WaitingPartyRepository>();
        services.AddScoped<ITableOccupancyChecker, TableOccupancyChecker>();
        services.AddScoped<IFloorUnitOfWork, FloorUnitOfWork>();
        services.AddScoped<SessionOrderLedger>();
        services.AddScoped<ITableSessionOrdersChecker>(provider => provider.GetRequiredService<SessionOrderLedger>());
        services.AddScoped<ITableSessionOrderLedger>(provider => provider.GetRequiredService<SessionOrderLedger>());
        services.AddOutboxDispatcher<FloorDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseFloorNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

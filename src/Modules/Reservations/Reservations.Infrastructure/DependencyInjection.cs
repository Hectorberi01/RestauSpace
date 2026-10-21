using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reservations.Application.Abstractions;
using Reservations.Infrastructure.Persistence;
using Reservations.Infrastructure.Workers;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reservations.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "reservations";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête),
    /// <c>IReservationTenants</c>, <c>IReservationLocations</c> et <c>IReservationFloor</c> ; sans contexte de tenant,
    /// toute requête sur les réservations ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddReservationsInfrastructure(
        this IServiceCollection services, string connectionString, Action<ReservationsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new ReservationsOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<ReservationsDbContext>(options => options.UseReservationsNpgsql(connectionString));
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IWaitlistRepository, WaitlistRepository>();
        services.AddScoped<IReservationsDueWork, ReservationsDueWork>();
        services.AddScoped<IReservationCustomerReassignment, ReservationCustomerReassignment>();
        services.AddScoped<IReservationPolicyStore, ReservationPolicyStore>();
        services.AddScoped<IReservationCapacityLock, ReservationCapacityLock>();
        services.AddScoped<IReservationsUnitOfWork, ReservationsUnitOfWork>();
        services.AddOutboxDispatcher<ReservationsDbContext>();
        services.AddSingleton<ReservationsDueWorker>();
        if (options.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<ReservationsDueWorker>());
        }

        return services;
    }

    internal static DbContextOptionsBuilder UseReservationsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

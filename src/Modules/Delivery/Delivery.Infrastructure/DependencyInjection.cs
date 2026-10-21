using Delivery.Application.Abstractions;
using Delivery.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Delivery.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "delivery";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête) et les
    /// ports vers Tenants, Locations et Orders ; sans contexte de tenant, toute requête ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddDeliveryInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<DeliveryDbContext>(options => options.UseDeliveryNpgsql(connectionString));
        services.AddScoped<IDeliverySettingsRepository, DeliverySettingsRepository>();
        services.AddScoped<IFulfillmentRepository, FulfillmentRepository>();
        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<ICourierRemittanceRepository, CourierRemittanceRepository>();
        services.AddScoped<IDeliveryUnitOfWork, DeliveryUnitOfWork>();
        services.AddOutboxDispatcher<DeliveryDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseDeliveryNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

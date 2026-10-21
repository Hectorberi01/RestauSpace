using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Orders.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "orders";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête),
    /// <c>IOrderTenants</c>, <c>IOrderLocations</c>, <c>IOrderTableSessions</c> et <c>IOrderCatalog</c> ; sans contexte
    /// de tenant, toute requête sur les commandes ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<OrdersDbContext>(options => options.UseOrdersNpgsql(connectionString));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderCustomerReassignment, OrderCustomerReassignment>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IOrdersUnitOfWork, OrdersUnitOfWork>();
        services.AddScoped<OrderPaymentSettlements>();
        services.AddScoped<IOrderPayments>(provider => provider.GetRequiredService<OrderPaymentSettlements>());
        services.AddScoped<IOrderPaymentSettlements>(provider => provider.GetRequiredService<OrderPaymentSettlements>());
        services.AddOutboxDispatcher<OrdersDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseOrdersNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

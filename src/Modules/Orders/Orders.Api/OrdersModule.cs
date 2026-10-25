using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orders.Application;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Orders.Api;

/// <summary>
/// Composition du module Orders, appelée par l'hôte. L'hôte fournit aussi <c>IOrderTenants</c> (Tenants),
/// <c>IOrderLocations</c> (Locations), <c>IOrderTableSessions</c> (Floor), <c>IOrderCatalog</c> (Catalog) et le tenant de
/// la requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services, string connectionString)
    {
        services.AddOrdersInfrastructure(connectionString);
        services.AddScoped<OrdersWorkspace>();
        services.TryAddScoped<IPlanFeatureGate, AllowAllPlanFeatures>();
        services.AddHandlersFrom(typeof(OrdersApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateOrdersDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using Kitchen.Application;
using Kitchen.Infrastructure;
using Kitchen.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Kitchen.Api;

/// <summary>
/// Composition du module Kitchen, appelée par l'hôte. L'hôte fournit aussi <c>IKitchenTenants</c> (Tenants),
/// <c>IKitchenLocations</c> (Locations), <c>IKitchenProducts</c> (Catalog), <c>IKitchenOrders</c> (Orders) et le tenant
/// courant (<c>AddRequestTenantContext</c>).
/// </summary>
public static class KitchenModule
{
    public static IServiceCollection AddKitchenModule(this IServiceCollection services, string connectionString)
    {
        services.AddKitchenInfrastructure(connectionString);
        services.AddScoped<KitchenWorkspace>();
        services.AddHandlersFrom(typeof(KitchenApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateKitchenDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KitchenDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

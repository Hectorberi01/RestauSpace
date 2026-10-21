using Inventory.Application;
using Inventory.Infrastructure;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Inventory.Api;

/// <summary>
/// Composition du module Inventory, appelée par l'hôte. L'hôte fournit aussi <c>IInventoryTenants</c> (Tenants),
/// <c>IInventoryLocations</c> (Locations), <c>IInventoryProducts</c> (Catalog), <c>IInventoryOrders</c> (Orders) et le
/// tenant courant (<c>AddRequestTenantContext</c>).
/// </summary>
public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, string connectionString)
    {
        services.AddInventoryInfrastructure(connectionString);
        services.AddScoped<InventoryWorkspace>();
        services.AddHandlersFrom(typeof(InventoryApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateInventoryDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

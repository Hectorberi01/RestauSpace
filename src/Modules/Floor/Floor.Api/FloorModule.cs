using Floor.Application;
using Floor.Infrastructure;
using Floor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Floor.Api;

/// <summary>
/// Composition du module Floor, appelée par l'hôte. L'hôte fournit aussi <c>IFloorTenants</c> (module Tenants),
/// <c>IFloorLocations</c> (module Locations) et le tenant de la requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class FloorModule
{
    public static IServiceCollection AddFloorModule(this IServiceCollection services, string connectionString)
    {
        services.AddFloorInfrastructure(connectionString);
        services.AddScoped<FloorWorkspace>();
        services.AddHandlersFrom(typeof(FloorApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateFloorDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FloorDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using Locations.Application;
using Locations.Infrastructure;
using Locations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Locations.Api;

/// <summary>
/// Composition du module Locations, appelée par l'hôte. L'hôte fournit aussi <c>ITenantDirectory</c> (issu du
/// module Tenants) et le tenant de la requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class LocationsModule
{
    public static IServiceCollection AddLocationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddLocationsInfrastructure(connectionString);
        services.AddScoped<LocationEditor>();
        services.AddHandlersFrom(typeof(LocationsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateLocationsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LocationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

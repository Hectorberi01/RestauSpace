using Catalog.Application;
using Catalog.Infrastructure;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Catalog.Api;

/// <summary>
/// Composition du module Catalog, appelée par l'hôte. L'hôte fournit aussi <c>ICatalogTenants</c> (module Tenants),
/// <c>ICatalogLocations</c> (module Locations) et le tenant de la requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddCatalogInfrastructure(connectionString);
        services.AddScoped<CatalogWorkspace>();
        services.AddHandlersFrom(typeof(CatalogApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateCatalogDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

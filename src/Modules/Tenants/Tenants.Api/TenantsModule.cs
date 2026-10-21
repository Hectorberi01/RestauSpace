using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;
using Tenants.Application.Commands;
using Tenants.Infrastructure;
using Tenants.Infrastructure.Persistence;

namespace Tenants.Api;

public static class TenantsModule
{
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, string connectionString)
    {
        services.AddTenantsInfrastructure(connectionString);

        services.AddHandlersFrom(typeof(CreateTenantHandler).Assembly);
        services.AddDemoRequestRateLimiting();
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateTenantsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

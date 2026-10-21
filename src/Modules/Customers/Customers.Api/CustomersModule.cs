using Customers.Application;
using Customers.Infrastructure;
using Customers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Customers.Api;

/// <summary>
/// Composition du module Customers, appelée par l'hôte. L'hôte fournit aussi <c>ICustomersTenants</c> (Tenants) et le
/// tenant courant (<c>AddRequestTenantContext</c>).
/// </summary>
public static class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, string connectionString)
    {
        services.AddCustomersInfrastructure(connectionString);
        services.AddScoped<CustomersWorkspace>();
        services.AddHandlersFrom(typeof(CustomersApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateCustomersDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CustomersDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using Loyalty.Application;
using Loyalty.Infrastructure;
using Loyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Loyalty.Api;

/// <summary>
/// Composition du module Loyalty, appelée par l'hôte. L'hôte fournit aussi <c>ILoyaltyTenants</c> (Tenants),
/// <c>ILoyaltyCustomers</c> (Customers), <c>ILoyaltyOrders</c> (Orders), le tenant courant (<c>AddRequestTenantContext</c>)
/// et les réactions aux événements des autres modules (outbox).
/// </summary>
public static class LoyaltyModule
{
    public static IServiceCollection AddLoyaltyModule(
        this IServiceCollection services, string connectionString, Action<LoyaltyOptions>? configure = null)
    {
        services.AddLoyaltyInfrastructure(connectionString, configure);
        services.AddScoped<LoyaltyWorkspace>();
        services.AddHandlersFrom(typeof(LoyaltyApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateLoyaltyDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

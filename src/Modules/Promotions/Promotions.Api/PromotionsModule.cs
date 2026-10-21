using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promotions.Application;
using Promotions.Infrastructure;
using Promotions.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Promotions.Api;

/// <summary>
/// Composition du module Promotions, appelée par l'hôte. L'hôte fournit aussi <c>IPromotionTenants</c> (Tenants),
/// <c>IPromotionOrders</c> (Orders), le tenant courant (<c>AddRequestTenantContext</c>) et les réactions aux événements
/// des autres modules (outbox).
/// </summary>
public static class PromotionsModule
{
    public static IServiceCollection AddPromotionsModule(
        this IServiceCollection services, string connectionString, Action<PromotionsOptions>? configure = null)
    {
        services.AddPromotionsInfrastructure(connectionString, configure);
        services.AddScoped<PromotionsWorkspace>();
        services.AddHandlersFrom(typeof(PromotionsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigratePromotionsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PromotionsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

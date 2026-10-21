using Delivery.Application;
using Delivery.Application.Abstractions;
using Delivery.Infrastructure;
using Delivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Delivery.Api;

/// <summary>
/// Composition du module Delivery, appelée par l'hôte. L'hôte fournit aussi <c>IDeliveryTenants</c> (Tenants),
/// <c>IDeliveryLocations</c> (Locations), <c>IDeliveryOrders</c> (Orders), <c>IDeliveryPayments</c> (Payments) et le tenant de la requête
/// (<c>AddRequestTenantContext</c>).
/// </summary>
public static class DeliveryModule
{
    public static IServiceCollection AddDeliveryModule(this IServiceCollection services, string connectionString)
    {
        services.AddDeliveryInfrastructure(connectionString);
        services.AddScoped<DeliveryWorkspace>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IDeliveryOperator, HttpDeliveryOperator>();
        services.AddHandlersFrom(typeof(DeliveryApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateDeliveryDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using Billing.Application;
using Billing.Infrastructure;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Billing.Api;

/// <summary>
/// Composition du module Billing, appelée par l'hôte. L'hôte fournit aussi <c>IBillingTenants</c> (Tenants), le tenant
/// courant (<c>AddRequestTenantContext</c>) et la répercussion des événements d'abonnement sur le restaurant (outbox).
/// </summary>
public static class BillingModule
{
    public static IServiceCollection AddBillingModule(
        this IServiceCollection services,
        string connectionString,
        Action<BillingOptions>? configure = null,
        Action<BillingSettings>? settings = null)
    {
        var billingSettings = new BillingSettings();
        settings?.Invoke(billingSettings);
        services.TryAddSingleton(billingSettings);

        services.AddBillingInfrastructure(connectionString, configure);
        services.AddScoped<BillingWorkspace>();
        services.AddScoped<Billing.Application.Commands.CheckoutDesk>();
        services.AddHandlersFrom(typeof(BillingApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateBillingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

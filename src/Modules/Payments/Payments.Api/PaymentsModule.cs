using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Application;
using Payments.Infrastructure;
using Payments.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Payments.Api;

/// <summary>
/// Composition du module Payments, appelée par l'hôte. L'hôte fournit aussi <c>IPaymentTenants</c> (Tenants),
/// <c>IPaymentOrders</c> (Orders) et le tenant courant (<c>AddRequestTenantContext</c>).
/// </summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services, string connectionString, Action<PaymentsOptions>? configure = null)
    {
        services.AddPaymentsInfrastructure(connectionString, configure);
        services.AddScoped<PaymentsWorkspace>();
        services.AddScoped<CashDesk>();
        services.AddScoped<Payments.Application.Commands.PlatformCollector>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICashier, HttpCashier>();
        services.TryAddScoped<IPaymentTenantCurrency, NoPaymentTenantCurrency>();
        services.TryAddScoped<IPaymentLocations, NoPaymentLocations>();
        services.AddHandlersFrom(typeof(PaymentsApplicationErrors).Assembly);
        services.AddConfigurationTestRateLimiting();
        return services;
    }

    /// <summary>
    /// Applique les migrations en attente du schéma du module, puis pose les moyens du comptoir intégrés dans le
    /// catalogue de la plateforme s'ils manquent.
    /// </summary>
    public static async Task MigratePaymentsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(cancellationToken);
        }

        await services.SeedBuiltInPaymentProvidersAsync(cancellationToken);
    }

    /// <summary>Tant que l'hôte n'a pas branché Tenants : devise inconnue, aucune caisse ne s'ouvre.</summary>
    private sealed class NoPaymentTenantCurrency : IPaymentTenantCurrency
    {
        public Task<Currency?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Currency?>(null);
    }

    /// <summary>Tant que l'hôte n'a pas branché Locations : aucun établissement n'est trouvé (fermé par défaut).</summary>
    private sealed class NoPaymentLocations : IPaymentLocations
    {
        public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocationReference?>(null);
    }
}

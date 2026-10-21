using Fiscal.Application;
using Fiscal.Infrastructure;
using Fiscal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Fiscal.Api;

/// <summary>
/// Composition du module Fiscal, appelée par l'hôte. L'hôte fournit aussi <c>IFiscalTenants</c> (Tenants),
/// <c>IFiscalOrders</c> (Orders et Payments) et le tenant courant (<c>AddRequestTenantContext</c>).
/// </summary>
public static class FiscalModule
{
    public static IServiceCollection AddFiscalModule(this IServiceCollection services, string connectionString, Action<FiscalOptions>? configure = null)
    {
        services.AddFiscalInfrastructure(connectionString, configure);
        services.AddScoped<FiscalWorkspace>();
        services.TryAddScoped<IFiscalOrders, NoFiscalOrders>();
        services.AddHandlersFrom(typeof(FiscalApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateFiscalDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FiscalDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Tant que l'hôte n'a pas branché Orders : aucune commande n'est trouvée, rien n'est facturé.</summary>
    private sealed class NoFiscalOrders : IFiscalOrders
    {
        public Task<FiscalOrder?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FiscalOrder?>(null);
    }
}

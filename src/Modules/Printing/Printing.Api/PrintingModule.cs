using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;
using Printing.Application;
using Printing.Application.Abstractions;
using Printing.Infrastructure;
using Printing.Infrastructure.Persistence;

namespace Printing.Api;

/// <summary>
/// Composition du module Printing, appelée par l'hôte. L'hôte fournit aussi <c>IPrintingTenants</c> (Tenants),
/// <c>IPrintingLocations</c> (Locations), le tenant courant (<c>AddRequestTenantContext</c>) et le schéma
/// d'authentification des imprimantes (<see cref="PrinterTokenAuthentication.AddPrinterTokens"/>).
/// </summary>
public static class PrintingModule
{
    public static IServiceCollection AddPrintingModule(this IServiceCollection services, string connectionString)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IPrinterClient, HttpPrinterClient>();
        services.AddPrintingInfrastructure(connectionString);
        services.AddScoped<PrintingWorkspace>();
        services.TryAddScoped<Printing.Application.Commands.IPrintingReceipts, NoReceipts>();
        services.AddHandlersFrom(typeof(PrintingApplicationErrors).Assembly);
        services.AddPrinterRateLimiting();
        return services;
    }

    /// <summary>Tant que l'hôte n'a pas branché Orders : aucune commande n'est trouvée.</summary>
    private sealed class NoReceipts : Printing.Application.Commands.IPrintingReceipts
    {
        public Task<Printing.Application.Commands.OrderReceiptInput?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Printing.Application.Commands.OrderReceiptInput?>(null);
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigratePrintingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PrintingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

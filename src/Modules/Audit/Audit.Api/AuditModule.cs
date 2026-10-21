using Audit.Application;
using Audit.Infrastructure;
using Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Audit.Api;

/// <summary>
/// Composition du module Audit, appelée par l'hôte. L'hôte fournit le tenant courant (<c>AddRequestTenantContext</c>) et
/// alimente le journal en traduisant les événements sensibles des autres modules (<c>RecordAuditEntryCommand</c>).
/// </summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString, Action<AuditOptions>? configure = null)
    {
        services.AddAuditInfrastructure(connectionString, configure);
        services.AddHandlersFrom(typeof(AuditApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module, puis pose les garanties d'ajout seul en base.</summary>
    public static async Task MigrateAuditDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await AuditDatabase.EnsureAppendOnlyAsync(context, cancellationToken);
    }
}

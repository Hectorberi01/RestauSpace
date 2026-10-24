using Backups.Application;
using Backups.Infrastructure;
using Backups.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Backups.Api;

/// <summary>Composition du module Backups, appelée par l'hôte, qui fournit aussi <c>IAdminTwoFactor</c> (Identity).</summary>
public static class BackupsModule
{
    public static IServiceCollection AddBackupsModule(
        this IServiceCollection services,
        string connectionString,
        Action<BackupsSettings> settings,
        Action<BackupsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var backupsSettings = new BackupsSettings();
        settings(backupsSettings);
        backupsSettings.Validate();
        services.TryAddSingleton(backupsSettings);

        services.AddBackupsInfrastructure(connectionString, configure);
        services.AddHandlersFrom(typeof(BackupsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateBackupsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

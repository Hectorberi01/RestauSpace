using Files.Application;
using Files.Infrastructure;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Files.Api;

/// <summary>
/// Composition du module Files, appelée par l'hôte. L'hôte fournit aussi <c>IFileTenants</c> (Tenants),
/// <c>IFileOwnerChecker</c> (Catalog, Locations, Tenants), le tenant courant (<c>AddRequestTenantContext</c>) et, s'il en
/// a un, l'antivirus.
/// </summary>
public static class FilesModule
{
    public static IServiceCollection AddFilesModule(
        this IServiceCollection services,
        string connectionString,
        Action<FilesOptions>? configure = null,
        Action<FilesSettings>? settings = null)
    {
        var filesSettings = new FilesSettings();
        settings?.Invoke(filesSettings);
        services.TryAddSingleton(filesSettings);

        services.AddFilesInfrastructure(connectionString, configure);
        services.AddScoped<FilesWorkspace>();
        services.AddHandlersFrom(typeof(FilesApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateFilesDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FilesDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using Files.Application;
using Files.Application.Abstractions;
using Files.Application.Storage;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Storage;
using Files.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Files.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni n'y fait de jointure.</summary>
    public const string Schema = "files";

    /// <summary>
    /// Enregistre la persistance et le stockage du module. L'hôte fournit <see cref="ITenantContext"/>,
    /// <c>IFileTenants</c> et <c>IFileOwnerChecker</c> ; il peut fournir un antivirus (<see cref="IFileScanner"/>).
    /// Stockage : R2 (réglages <see cref="R2Options"/> obligatoires), disque local de développement ou en mémoire.
    /// </summary>
    public static IServiceCollection AddFilesInfrastructure(
        this IServiceCollection services, string connectionString, Action<FilesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var filesOptions = new FilesOptions();
        configure?.Invoke(filesOptions);
        services.TryAddSingleton(filesOptions);
        services.TryAddSingleton(new FilesSettings());
        services.TryAddSingleton(new PublicMediaLinks(filesOptions.PublicMedia.BaseUrl, filesOptions.PublicMedia.Key()));

        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<FilesDbContext>(options => options.UseFilesNpgsql(connectionString));
        services.AddScoped<IMediaFileRepository, MediaFileRepository>();
        services.AddScoped<IAbandonedUploads, AbandonedUploads>();
        services.AddScoped<IFilesUnitOfWork, FilesUnitOfWork>();
        services.TryAddScoped<IFileScanner, NoFileScanner>();

        if (filesOptions.Storage == FileStorageProvider.Memory)
        {
            services.TryAddSingleton<InMemoryObjectStorage>();
            services.TryAddSingleton<IObjectStorage>(provider => provider.GetRequiredService<InMemoryObjectStorage>());
        }
        else if (filesOptions.Storage == FileStorageProvider.Local)
        {
            services.TryAddSingleton(provider => new LocalObjectStorage(filesOptions.Local, provider.GetRequiredService<IClock>()));
            services.TryAddSingleton<IObjectStorage>(provider => provider.GetRequiredService<LocalObjectStorage>());
        }
        else
        {
            filesOptions.R2.Validate();
            services.TryAddSingleton<IObjectStorage>(provider => new R2ObjectStorage(
                new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(60) },
                filesOptions,
                provider.GetRequiredService<IClock>()));
        }

        services.AddScoped<IOutboxConsumer, StoredObjectCleanup>();
        services.AddSingleton<AbandonedUploadsWorker>();
        if (filesOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<AbandonedUploadsWorker>());
        }

        services.AddOutboxDispatcher<FilesDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseFilesNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

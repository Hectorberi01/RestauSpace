using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Promotions.Application.Abstractions;
using Promotions.Domain;
using Promotions.Infrastructure.Persistence;
using Promotions.Infrastructure.Workers;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Promotions.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni n'y fait de jointure.</summary>
    public const string Schema = "promotions";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité), <c>IPromotionTenants</c> et <c>IPromotionOrders</c> ; sans contexte de tenant, toute requête ne
    /// renvoie rien.
    /// </summary>
    public static IServiceCollection AddPromotionsInfrastructure(
        this IServiceCollection services, string connectionString, Action<PromotionsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var promotionsOptions = new PromotionsOptions();
        configure?.Invoke(promotionsOptions);
        services.TryAddSingleton(promotionsOptions);

        // Hors requête (job d'expiration), le tenant du scope est celui fixé par le job.
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<PromotionsDbContext>(options => options.UsePromotionsNpgsql(connectionString));
        services.AddScoped<PromotionUsageLedger>();
        services.AddScoped<IPromotionUsageLedger>(provider => provider.GetRequiredService<PromotionUsageLedger>());
        services.AddScoped<IOrderPromotionUsages>(provider => provider.GetRequiredService<PromotionUsageLedger>());
        services.AddScoped<CouponRepository>();
        services.AddScoped<ICouponRepository>(provider => provider.GetRequiredService<CouponRepository>());
        services.AddScoped<ICouponCodeUniquenessChecker>(provider => provider.GetRequiredService<CouponRepository>());
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IPromotionExpiryDirectory, PromotionExpiryDirectory>();
        services.AddScoped<IPromotionsUnitOfWork, PromotionsUnitOfWork>();
        services.AddSingleton<PromotionExpiryWorker>();
        if (promotionsOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<PromotionExpiryWorker>());
        }

        services.AddOutboxDispatcher<PromotionsDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UsePromotionsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

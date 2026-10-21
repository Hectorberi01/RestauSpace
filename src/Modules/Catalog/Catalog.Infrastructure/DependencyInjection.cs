using Catalog.Application.Abstractions;
using Catalog.Domain;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Catalog.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "catalog";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête),
    /// <c>ICatalogTenants</c> et <c>ICatalogLocations</c> ; sans contexte de tenant, toute requête sur le catalogue ne
    /// renvoie rien.
    /// </summary>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<CatalogDbContext>(options => options.UseCatalogNpgsql(connectionString));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IModifierGroupRepository, ModifierGroupRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<ITaxCategoryRepository, TaxCategoryRepository>();
        services.AddScoped<ITaxCategoryCodeUniquenessChecker, TaxCategoryCodeUniquenessChecker>();
        services.AddScoped<IPricedCatalogLookup, PricedCatalogLookup>();
        services.AddScoped<IPublicMenuLookup, PublicMenuLookup>();
        services.AddScoped<ICatalogUnitOfWork, CatalogUnitOfWork>();
        services.AddOutboxDispatcher<CatalogDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseCatalogNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;
using Tenants.Infrastructure.Persistence;

namespace Tenants.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTenantsInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TenantsDbContext>(options => options.UseTenantsNpgsql(connectionString));
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IDemoRequestRepository, DemoRequestRepository>();
        services.AddScoped<ITenantsUnitOfWork, TenantsUnitOfWork>();
        services.AddScoped<PricedCatalogLedger>();
        services.AddScoped<ITenantPricedCatalogChecker>(provider => provider.GetRequiredService<PricedCatalogLedger>());
        services.AddScoped<IPricedCatalogLedger>(provider => provider.GetRequiredService<PricedCatalogLedger>());
        services.AddOutboxDispatcher<TenantsDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseTenantsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TenantsDbContext.Schema));
}

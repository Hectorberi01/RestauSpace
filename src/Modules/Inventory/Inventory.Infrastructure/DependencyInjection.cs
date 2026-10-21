using Inventory.Application.Abstractions;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ses tables ni ne fait de jointure
    /// dessus.</summary>
    public const string Schema = "inventory";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité), <c>IInventoryTenants</c>, <c>IInventoryLocations</c>, <c>IInventoryProducts</c> et
    /// <c>IInventoryOrders</c> ; sans contexte de tenant, toute requête sur le stock ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<InventoryDbContext>(options => options.UseInventoryNpgsql(connectionString));
        services.AddScoped<IIngredientRepository, IngredientRepository>();
        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
        services.AddScoped<IStockMovementLedger, StockMovementLedger>();
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IOrderConsumptionLog, OrderConsumptionLog>();
        services.AddScoped<IIngredientNameUniquenessChecker, IngredientNameUniquenessChecker>();
        services.AddScoped<IInventoryItemUniquenessChecker, InventoryItemUniquenessChecker>();
        services.AddScoped<IRecipeUniquenessChecker, RecipeUniquenessChecker>();
        services.AddScoped<IInventoryUnitOfWork, InventoryUnitOfWork>();
        services.AddOutboxDispatcher<InventoryDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseInventoryNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

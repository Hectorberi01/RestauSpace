using Inventory.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Inventory.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Inventory : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les ingrédients, articles de stock, recettes et consommations de commandes est filtrée sur le tenant courant
/// (<see cref="TenantDbContext"/>) ; le registre des mouvements est toujours lu par (tenant, article).
/// </summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    /// <summary>Registre : on y insère (par les articles), on n'y modifie ni ne supprime jamais rien.</summary>
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    public DbSet<Recipe> Recipes => Set<Recipe>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    internal DbSet<OrderConsumption> OrderConsumptions => Set<OrderConsumption>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

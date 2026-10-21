using Catalog.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Catalog : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les produits, groupes d'options, menus et catégories fiscales est filtrée sur le tenant de la requête
/// (<see cref="TenantDbContext"/>) ; seule la vérification « catalogue tarifé » pour Tenants contourne ce filtre, avec
/// un tenant explicite.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();

    public DbSet<Menu> Menus => Set<Menu>();

    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

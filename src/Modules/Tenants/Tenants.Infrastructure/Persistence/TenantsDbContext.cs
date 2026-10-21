using Tenants.Domain.Prospects;
using Tenants.Domain.Tenants;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Tenants.Infrastructure.Persistence;

/// <summary>Base de données du module Tenants : son propre schéma, jamais joint aux tables d'un autre module.</summary>
public sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options) : DbContext(options)
{
    public const string Schema = "tenants";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<DemoRequest> DemoRequests => Set<DemoRequest>();

    internal DbSet<PricedCatalog> PricedCatalogs => Set<PricedCatalog>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
    }
}

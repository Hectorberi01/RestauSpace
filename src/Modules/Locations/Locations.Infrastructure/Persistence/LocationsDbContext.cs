using Locations.Domain.Locations;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Locations.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Locations : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les établissements est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class LocationsDbContext(DbContextOptions<LocationsDbContext> options, ITenantContext tenantContext): TenantDbContext(options, tenantContext)
{
    public DbSet<RestaurantLocation> Locations => Set<RestaurantLocation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LocationsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

using Kitchen.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Kitchen.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Kitchen : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les postes, routages et tickets est filtrée sur le tenant courant (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class KitchenDbContext(DbContextOptions<KitchenDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<KitchenStation> Stations => Set<KitchenStation>();

    public DbSet<StationRouting> Routings => Set<StationRouting>();

    public DbSet<KitchenTicket> Tickets => Set<KitchenTicket>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KitchenDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

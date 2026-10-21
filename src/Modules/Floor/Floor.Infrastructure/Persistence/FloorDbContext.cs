using Floor.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Floor.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Floor : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les zones et les sessions est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class FloorDbContext(DbContextOptions<FloorDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<DiningArea> DiningAreas => Set<DiningArea>();

    public DbSet<TableSession> TableSessions => Set<TableSession>();

    public DbSet<WaitingParty> WaitingParties => Set<WaitingParty>();

    internal DbSet<SessionOrder> SessionOrders => Set<SessionOrder>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FloorDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

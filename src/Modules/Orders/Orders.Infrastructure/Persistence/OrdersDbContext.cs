using Orders.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Orders : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les commandes est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Order> Orders => Set<Order>();

    internal DbSet<OrderNumberCounter> NumberCounters => Set<OrderNumberCounter>();

    internal DbSet<OrderPaymentSettlement> PaymentSettlements => Set<OrderPaymentSettlement>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

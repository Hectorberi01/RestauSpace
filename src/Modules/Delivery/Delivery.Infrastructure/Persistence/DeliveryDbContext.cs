using Delivery.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Delivery.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Delivery : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les réglages, les fiches, les livreurs et les remises est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<DeliverySettings> Settings => Set<DeliverySettings>();

    public DbSet<Fulfillment> Fulfillments => Set<Fulfillment>();

    public DbSet<Courier> Couriers => Set<Courier>();

    public DbSet<CourierRemittance> Remittances => Set<CourierRemittance>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveryDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

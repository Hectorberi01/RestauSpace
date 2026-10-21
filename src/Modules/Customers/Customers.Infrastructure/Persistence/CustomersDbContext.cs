using Customers.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Customers.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Customers : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les clients, consentements et réglages est filtrée sur le tenant courant (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<CustomerNote> Notes => Set<CustomerNote>();

    internal DbSet<CustomerSettings> Settings => Set<CustomerSettings>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomersDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

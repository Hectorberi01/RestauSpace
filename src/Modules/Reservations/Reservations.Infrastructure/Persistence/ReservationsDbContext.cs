using Reservations.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reservations.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Reservations : son propre schéma, jamais joint aux tables d'un autre module. Toute requête
/// sur les réservations et les politiques est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class ReservationsDbContext(DbContextOptions<ReservationsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();

    internal DbSet<ReservationPolicyRow> Policies => Set<ReservationPolicyRow>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservationsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

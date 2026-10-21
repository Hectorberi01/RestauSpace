using Loyalty.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Loyalty.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Loyalty : son propre schéma, jamais joint aux tables d'un autre module. Comptes,
/// récompenses ET écritures du registre sont filtrés sur le tenant courant, et rien n'est visible sans tenant.
/// </summary>
public sealed class LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<LoyaltyAccount> Accounts => Set<LoyaltyAccount>();

    public DbSet<Reward> Rewards => Set<Reward>();

    public DbSet<StoredValueCard> StoredValueCards => Set<StoredValueCard>();

    /// <summary>Le registre, lu seul (doublons, contre-passations, historique) : jamais réhydraté dans un compte.</summary>
    public DbSet<LoyaltyTransaction> Transactions => Set<LoyaltyTransaction>();

    internal DbSet<LoyaltyProgramSettings> Programs => Set<LoyaltyProgramSettings>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LoyaltyDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);

        // Une écriture n'est pas une racine (pas ITenantOwned) mais elle porte son tenant et se lit seule : même filtre.
        modelBuilder.Entity<LoyaltyTransaction>().HasQueryFilter(transaction => transaction.TenantId == CurrentTenantFilter);
    }
}

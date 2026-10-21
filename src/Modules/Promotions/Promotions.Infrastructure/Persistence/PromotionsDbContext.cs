using Promotions.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Promotions.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Promotions : son propre schéma, jamais joint aux tables d'un autre module. Promotions,
/// coupons et registres d'usages appartiennent tous à un tenant : toute requête est filtrée sur le tenant courant
/// (<see cref="TenantDbContext"/>), et ne renvoie rien sans tenant.
/// </summary>
public sealed class PromotionsDbContext(DbContextOptions<PromotionsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<Coupon> Coupons => Set<Coupon>();

    internal DbSet<PromotionUsage> PromotionUsages => Set<PromotionUsage>();

    internal DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromotionsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

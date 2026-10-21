using Promotions.Application.Abstractions;
using Promotions.Domain;

namespace Promotions.Infrastructure.Persistence;

/// <summary>
/// Implémentation de <see cref="IPromotionUsageLedger"/> sur les tables <c>promotion_usages</c> et
/// <c>coupon_usages</c>. La ligne trouvée est CHARGÉE ET SUIVIE : si la commande la rend ensuite
/// (<c>RevertUsage</c>, <c>Release</c>), l'unit of work la marque rendue sans nouvelle lecture. Une ligne ajoutée dans
/// la même unité de travail (pas encore écrite) compte aussi : deux applications dans la même requête sont refusées.
/// </summary>
internal sealed class PromotionUsageLedger(PromotionsDbContext context) : IPromotionUsageLedger, IOrderPromotionUsages
{
    public async Task<bool> HasUsageForOrderAsync(
        TenantId tenantId, PromotionId promotionId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (context.PromotionUsages.Local.Any(usage =>
                usage.IsHeld && usage.TenantId == tenantId && usage.PromotionId == promotionId && usage.OrderId == orderId))
        {
            return true;
        }

        var held = await context.PromotionUsages
            .Where(usage => usage.TenantId == tenantId && usage.PromotionId == promotionId && usage.OrderId == orderId)
            .Where(usage => usage.RevertedAtUtc == null)
            .FirstOrDefaultAsync(cancellationToken);
        // Ligne déjà suivie et rendue dans cette unité de travail : la base ne le sait pas encore.
        return held is { IsHeld: true };
    }

    public async Task<bool> HasUsageForOrderAsync(
        TenantId tenantId, CouponId couponId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (context.CouponUsages.Local.Any(usage =>
                usage.IsHeld && usage.TenantId == tenantId && usage.CouponId == couponId && usage.OrderId == orderId))
        {
            return true;
        }

        var held = await context.CouponUsages
            .Where(usage => usage.TenantId == tenantId && usage.CouponId == couponId && usage.OrderId == orderId)
            .Where(usage => usage.ReleasedAtUtc == null)
            .FirstOrDefaultAsync(cancellationToken);
        // Ligne déjà suivie et rendue dans cette unité de travail : la base ne le sait pas encore.
        return held is { IsHeld: true };
    }

    /// <summary>Lignes non rendues de la commande, SUIVIES : les rendre ensuite ne demande pas de nouvelle lecture.</summary>
    public async Task<IReadOnlyList<PromotionId>> PromotionsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        var held = await context.PromotionUsages
            .Where(usage => usage.TenantId == tenantId && usage.OrderId == orderId && usage.RevertedAtUtc == null)
            .ToListAsync(cancellationToken);
        return held.Where(usage => usage.IsHeld).Select(usage => usage.PromotionId).Distinct().ToList();
    }

    /// <inheritdoc cref="PromotionsHeldByAsync"/>
    public async Task<IReadOnlyList<CouponId>> CouponsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        var held = await context.CouponUsages
            .Where(usage => usage.TenantId == tenantId && usage.OrderId == orderId && usage.ReleasedAtUtc == null)
            .ToListAsync(cancellationToken);
        return held.Where(usage => usage.IsHeld).Select(usage => usage.CouponId).Distinct().ToList();
    }
}

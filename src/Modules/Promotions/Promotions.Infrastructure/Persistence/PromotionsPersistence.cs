using Promotions.Application.Abstractions;
using Promotions.Domain;
using Promotions.Infrastructure.Persistence.Configurations;

namespace Promotions.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class PromotionRepository(PromotionsDbContext context) : IPromotionRepository
{
    public Task<Promotion?> FindAsync(TenantId tenantId, PromotionId id, CancellationToken cancellationToken = default) =>
        context.Promotions.SingleOrDefaultAsync(promotion => promotion.TenantId == tenantId && promotion.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Promotion>> ListAsync(
        TenantId tenantId, PromotionStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Promotions.AsNoTracking().Where(promotion => promotion.TenantId == tenantId);
        if (status is { } wanted)
        {
            query = query.Where(promotion => promotion.Status == wanted);
        }

        return await query
            .OrderByDescending(promotion => promotion.CreatedAtUtc)
            .ThenBy(promotion => promotion.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    // Sur les colonnes recopiées des règles (index tenant, statut, fin de période).
    public async Task<IReadOnlyList<Promotion>> ListAutomaticAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        await context.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.TenantId == tenantId && promotion.Status == PromotionStatus.Active)
            .Where(promotion => !EF.Property<bool>(promotion, PromotionConfiguration.RequiresCouponProperty))
            .Where(promotion => EF.Property<DateTimeOffset>(promotion, PromotionConfiguration.ValidFromProperty) <= nowUtc
                && EF.Property<DateTimeOffset>(promotion, PromotionConfiguration.ValidUntilProperty) > nowUtc)
            .OrderBy(promotion => promotion.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Promotion>> ListEndedAsync(TenantId tenantId, DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        await context.Promotions
            .Where(promotion => promotion.TenantId == tenantId && promotion.Status != PromotionStatus.Expired)
            .Where(promotion => EF.Property<DateTimeOffset>(promotion, PromotionConfiguration.ValidUntilProperty) <= nowUtc)
            .OrderBy(promotion => EF.Property<DateTimeOffset>(promotion, PromotionConfiguration.ValidUntilProperty))
            .ThenBy(promotion => promotion.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(Promotion promotion) => context.Promotions.Add(promotion);
}

internal sealed class CouponRepository(PromotionsDbContext context) : ICouponRepository, ICouponCodeUniquenessChecker
{
    public Task<Coupon?> FindAsync(TenantId tenantId, CouponId id, CancellationToken cancellationToken = default) =>
        context.Coupons.SingleOrDefaultAsync(coupon => coupon.TenantId == tenantId && coupon.Id == id, cancellationToken);

    public Task<Coupon?> FindByCodeAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken = default) =>
        context.Coupons.SingleOrDefaultAsync(coupon => coupon.TenantId == tenantId && coupon.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Coupon>> ListForPromotionAsync(TenantId tenantId, PromotionId promotionId, CancellationToken cancellationToken = default) =>
        (await context.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.TenantId == tenantId && coupon.PromotionId == promotionId)
            .ToListAsync(cancellationToken))
        .OrderBy(coupon => coupon.Code.Value, StringComparer.Ordinal)
        .ToList();

    public void Add(Coupon coupon) => context.Coupons.Add(coupon);

    public async Task<bool> IsUniqueAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken = default) =>
        !context.Coupons.Local.Any(coupon => coupon.TenantId == tenantId && coupon.Code == code)
        && !await context.Coupons.AnyAsync(coupon => coupon.TenantId == tenantId && coupon.Code == code, cancellationToken);
}

/// <summary>Tous tenants (job hors requête) : seuls des ids de tenant sortent d'ici.</summary>
internal sealed class PromotionExpiryDirectory(PromotionsDbContext context) : IPromotionExpiryDirectory
{
    public async Task<IReadOnlyList<TenantId>> ListTenantsWithEndedPromotionsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        await context.Promotions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(promotion => promotion.Status != PromotionStatus.Expired)
            .Where(promotion => EF.Property<DateTimeOffset>(promotion, PromotionConfiguration.ValidUntilProperty) <= nowUtc)
            .Select(promotion => promotion.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
}

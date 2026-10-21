using Promotions.Domain;

namespace Promotions.Application.Queries;

public sealed record BenefitDto(BenefitType Type, decimal? Percentage, decimal? Amount, decimal? MaxDiscount);

/// <param name="CurrencyCode">Devise des montants des règles ; null pour un pourcentage sans plafond ni minimum.</param>
public sealed record PromotionRulesDto(
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    BenefitDto? Benefit,
    string? CurrencyCode,
    decimal? MinimumOrderAmount,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<OrderType> OrderTypes,
    int? UsageLimit,
    bool RequiresCoupon)
{
    public static PromotionRulesDto From(PromotionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var benefit = rules.Benefit;
        return new(
            rules.ValidityPeriod.StartUtc,
            rules.ValidityPeriod.EndUtc,
            benefit is null ? null : new BenefitDto(benefit.Type, benefit.Percentage, benefit.Amount?.Amount, benefit.MaxDiscount?.Amount),
            rules.Currency?.Code,
            rules.Conditions.MinimumOrderAmount?.Amount,
            rules.Conditions.ProductIds.Select(id => id.Value).ToList(),
            rules.Conditions.LocationIds.Select(id => id.Value).ToList(),
            rules.Conditions.OrderTypes.ToList(),
            rules.UsageLimit?.Value,
            rules.RequiresCoupon);
    }
}

public sealed record PromotionDto(
    Guid Id, string Name, PromotionStatus Status, PromotionRulesDto Rules, int UsageCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiredAtUtc)
{
    public static PromotionDto From(Promotion promotion)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        return new(
            promotion.Id.Value, promotion.Name, promotion.Status, PromotionRulesDto.From(promotion.Rules), promotion.UsageCount,
            promotion.CreatedAtUtc, promotion.ExpiredAtUtc);
    }
}

public sealed record CouponDto(Guid Id, Guid PromotionId, string Code, int MaxUses, int UsageCount, int RemainingUses, CouponStatus Status, DateTimeOffset CreatedAtUtc)
{
    public static CouponDto From(Coupon coupon)
    {
        ArgumentNullException.ThrowIfNull(coupon);
        return new(
            coupon.Id.Value, coupon.PromotionId.Value, coupon.Code.Value, coupon.MaxUses, coupon.UsageCount, coupon.RemainingUses,
            coupon.Status, coupon.CreatedAtUtc);
    }
}

/// <summary>
/// Ce qu'une promotion donnerait à une commande, sans rien enregistrer. <paramref name="Reason"/> : code stable de
/// l'inéligibilité (<c>promotions.discount.*</c>, <c>promotions.promotion.usage_limit_reached</c>...) ; null si éligible.
/// </summary>
public sealed record DiscountPreviewDto(Guid PromotionId, string Name, bool IsEligible, string? Reason, decimal Discount, string CurrencyCode);

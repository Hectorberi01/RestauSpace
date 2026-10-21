namespace Promotions.Domain;

/// <summary>
/// Ce qu'un autre agrégat (un coupon) doit savoir d'une promotion. Obtenable uniquement à partir d'une vraie promotion
/// via <see cref="Promotion.ToReference"/> : le constructeur est internal et rien ne peut être modifié avec
/// <c>with</c>, donc un appelant ne peut pas forger un tenant, un id ni un indicateur « non expirée ».
/// </summary>
public sealed record PromotionReference
{
    internal PromotionReference(TenantId tenantId, PromotionId promotionId, bool isExpired)
    {
        TenantId = tenantId;
        PromotionId = promotionId;
        IsExpired = isExpired;
    }

    public TenantId TenantId { get; }

    public PromotionId PromotionId { get; }

    public bool IsExpired { get; }
}

namespace Promotions.Domain;

/// <summary>
/// Port répondant à « cette commande détient-elle déjà une utilisation ? ». Un compteur ne peut pas le prouver et les
/// agrégats ne gardent aucune liste par commande : l'Application stocke une ligne par (promotion, commande) et par
/// (coupon, commande), écrite dans la même transaction que l'agrégat. L'index unique en base sur ces lignes reste le
/// dernier rempart.
/// </summary>
public interface IPromotionUsageLedger
{
    Task<bool> HasUsageForOrderAsync(TenantId tenantId, PromotionId promotionId, OrderId orderId, CancellationToken cancellationToken = default);

    Task<bool> HasUsageForOrderAsync(TenantId tenantId, CouponId couponId, OrderId orderId, CancellationToken cancellationToken = default);
}

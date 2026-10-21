namespace Promotions.Domain;

/// <summary>Port répondant à « ce code est-il libre pour le tenant ? » ; l'index unique en base reste le garde-fou.</summary>
public interface ICouponCodeUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken = default);
}

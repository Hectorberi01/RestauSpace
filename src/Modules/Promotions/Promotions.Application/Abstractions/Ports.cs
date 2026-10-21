using Promotions.Domain;

namespace Promotions.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IPromotionRepository
{
    Task<Promotion?> FindAsync(TenantId tenantId, PromotionId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<Promotion>> ListAsync(
        TenantId tenantId, PromotionStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lecture seule : les promotions qui s'appliquent SANS coupon et peuvent valoir à <paramref name="nowUtc"/>
    /// (actives, dans leur période). La décision finale reste celle du domaine.
    /// </summary>
    Task<IReadOnlyList<Promotion>> ListAutomaticAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Promotions non expirées dont la période est terminée à <paramref name="nowUtc"/>, à expirer (suivies).</summary>
    Task<IReadOnlyList<Promotion>> ListEndedAsync(TenantId tenantId, DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);

    void Add(Promotion promotion);
}

public interface ICouponRepository
{
    Task<Coupon?> FindAsync(TenantId tenantId, CouponId id, CancellationToken cancellationToken = default);

    /// <summary>Le coupon portant ce code (déjà normalisé) dans ce restaurant.</summary>
    Task<Coupon?> FindByCodeAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par code.</summary>
    Task<IReadOnlyList<Coupon>> ListForPromotionAsync(TenantId tenantId, PromotionId promotionId, CancellationToken cancellationToken = default);

    void Add(Coupon coupon);
}

/// <summary>Ce qu'une commande détient encore (usages non rendus), pour tout rendre à son annulation.</summary>
public interface IOrderPromotionUsages
{
    Task<IReadOnlyList<PromotionId>> PromotionsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CouponId>> CouponsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pour le job d'expiration SEULEMENT (hors requête, tous tenants) : les restaurants ayant une promotion non expirée dont
/// la période est terminée. Ne renvoie que des ids : le traitement se fait ensuite tenant par tenant.
/// </summary>
public interface IPromotionExpiryDirectory
{
    Task<IReadOnlyList<TenantId>> ListTenantsWithEndedPromotionsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Promotions doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IPromotionTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Une ligne de commande : son produit et son total (prix × quantité, options comprises, dans le mode de prix de la commande).</summary>
public sealed record PromotionOrderLine(ProductId ProductId, decimal LineTotal);

/// <summary>
/// Ce que Promotions doit savoir d'une commande pour l'évaluer : établissement, canal, devise, lignes, et si elle
/// accepte encore une remise (brouillon). Fourni par l'hôte depuis le module Orders.
/// </summary>
public sealed record PromotionOrder(
    OrderId OrderId, LocationId LocationId, OrderType Type, string CurrencyCode, bool AcceptsDiscounts, IReadOnlyList<PromotionOrderLine> Lines);

public interface IPromotionOrders
{
    Task<PromotionOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

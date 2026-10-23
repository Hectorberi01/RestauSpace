namespace Promotions.Application;

/// <summary>Erreurs des cas d'usage Promotions (les erreurs des règles métier vivent dans Promotions.Domain).</summary>
public static class PromotionsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("promotions.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError TenantNotWritable =
        new("promotions.tenant.not_writable", "The restaurant account is suspended or closed: its promotions can be read, not changed.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError PromotionNotFound = new("promotions.promotion.not_found", "The promotion does not exist.");
    public static readonly DomainError CouponNotFound = new("promotions.coupon.not_found", "The coupon does not exist.");
    public static readonly DomainError OrderNotFound = new("promotions.order.not_found", "The order does not exist.");
    public static readonly DomainError LocationNotFound = new("promotions.location.not_found", "The location does not exist.");
    public static readonly DomainError ProductNotFound = new("promotions.product.not_found", "The product does not exist.");

    public static readonly DomainError OrderNotEditable = new("promotions.order.not_editable", "The order no longer accepts a discount.");
    public static readonly DomainError TargetRequired =
        new("promotions.apply.target_required", "Give either a promotion or a coupon code, not both.");
    public static readonly DomainError BenefitInvalid =
        new("promotions.benefit.invalid", "A benefit is a percentage (with an optional cap) or a fixed amount, with a currency for any amount.");
    public static readonly DomainError PageInvalid = new("promotions.page_invalid", "Skip must be zero or more and take between 1 and 100.");
}

/// <summary>Codes de permission déclarés par le module Promotions (accordés via les rôles Staff).</summary>
public static class PromotionsPermissions
{
    /// <summary>Consulter les promotions, leurs coupons et prévisualiser les remises d'une commande.</summary>
    public const string View = "promotions.view";

    /// <summary>Au comptoir : appliquer une promotion ou un coupon à une commande en cours.</summary>
    public const string Apply = "promotions.apply";

    /// <summary>Créer et modifier les promotions et leurs coupons, les activer, suspendre ou arrêter.</summary>
    public const string Manage = "promotions.manage";

    public static IReadOnlyList<string> All { get; } = [View, Apply, Manage];
}

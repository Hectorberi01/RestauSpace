namespace Loyalty.Application;

/// <summary>Erreurs des cas d'usage Loyalty (celles des règles métier sont dans Loyalty.Domain).</summary>
public static class LoyaltyApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("loyalty.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError TenantNotWritable =
        new("loyalty.tenant.not_writable", "The restaurant account is suspended or closed: its loyalty data can be read, not changed.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError AccountNotFound = new("loyalty.account.not_found", "The loyalty account does not exist.");
    public static readonly DomainError RewardNotFound = new("loyalty.reward.not_found", "The reward does not exist.");
    public static readonly DomainError CustomerNotFound = new("loyalty.customer.not_found", "The customer does not exist.");
    public static readonly DomainError OrderNotFound = new("loyalty.order.not_found", "The order does not exist.");

    public static readonly DomainError OrderOfAnotherCustomer =
        new("loyalty.order.other_customer", "A reward can only be used on an order of the account's customer.");
    public static readonly DomainError OrderNotEditable =
        new("loyalty.order.not_editable", "The order no longer accepts a discount.");
    public static readonly DomainError RewardCurrencyMismatch =
        new("loyalty.reward.currency_mismatch", "The reward discount is not in the order currency.");

    public static readonly DomainError PageInvalid = new("loyalty.page_invalid", "Skip must be zero or more and take between 1 and 100.");
}

/// <summary>Codes de permission déclarés par le module Loyalty (accordés via les rôles Staff).</summary>
public static class LoyaltyPermissions
{
    /// <summary>Consulter les comptes, leur historique, le catalogue de récompenses et le barème.</summary>
    public const string View = "loyalty.view";

    /// <summary>Au comptoir : ouvrir le compte d'un client et lui faire utiliser une récompense sur sa commande.</summary>
    public const string Operate = "loyalty.operate";

    /// <summary>Corrections : ajuster un solde, suspendre, réactiver ou clore un compte.</summary>
    public const string Manage = "loyalty.manage";

    /// <summary>Barème du programme et catalogue des récompenses.</summary>
    public const string Configure = "loyalty.configure";

    public static IReadOnlyList<string> All { get; } = [View, Operate, Manage, Configure];
}

/// <summary>Motifs écrits au registre par les réactions automatiques (jamais saisis par une personne).</summary>
public static class LoyaltyReasons
{
    public const string OrderCancelled = "order_cancelled";
    public const string CustomerAnonymized = "customer_anonymized";
    public const string CustomerMerged = "customer_merged";
    public const string DiscountNotApplied = "discount_not_applied";
}

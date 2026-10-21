namespace Storefront.Application;

public static class StorefrontApplicationErrors
{
    public static readonly DomainError HumanCheckUnavailable =
        new("storefront.human_check.unavailable", "The anti-robot check is not set up on the platform yet.");

    public static readonly DomainError HumanCheckFailed =
        new("storefront.human_check.failed", "The anti-robot check failed; try again.");

    public static readonly DomainError ConcurrentUpdate =
        new("storefront.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError TenantNotWritable =
        new("storefront.tenant.not_writable", "The restaurant account is suspended or closed: its online sales can be read, not changed.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError AppNotFound = new("storefront.app.not_found", "The sales application does not exist.");
    public static readonly DomainError LocationNotFound = new("storefront.location.not_found", "The location does not exist.");

    public static readonly DomainError OptionRequired =
        new("storefront.option_required", "Online sales through this channel are an option of your subscription: add it first.");

    public static readonly DomainError ClientRequired = new("storefront.client_required", "A publishable key is required.");
}

/// <summary>Codes de permission déclarés par le module Storefront (accordés via les rôles Staff).</summary>
public static class StorefrontPermissions
{
    /// <summary>Voir les applications de vente et les réglages de vente en ligne.</summary>
    public const string View = "storefront.view";

    /// <summary>Créer les applications et leurs clés, régler la vente en ligne, mettre en pause.</summary>
    public const string Manage = "storefront.manage";

    /// <summary>Envoyer le reçu d'une commande au client (WhatsApp, SMS) ou en obtenir le lien.</summary>
    public const string SendReceipts = "storefront.receipts";

    public static IReadOnlyList<string> All { get; } = [View, Manage, SendReceipts];
}

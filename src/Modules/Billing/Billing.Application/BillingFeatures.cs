using Billing.Domain;

namespace Billing.Application;

/// <summary>
/// Le catalogue des fonctionnalités vendables, déclarées ici sous forme de constantes et étiquetées par type de quota :
/// <list type="bullet">
/// <item><b>consommation</b> : compté par période (mois civil UTC) dans un <see cref="UsageCounter"/>, remis à zéro à
/// chaque période, appliqué par <c>ConsumeFeatureCommand</c> ou mesuré par le module qui en a la charge ;</item>
/// <item><b>stock</b> : pas de compteur ; le module concerné compte l'existant et demande la limite
/// (<c>GetFeatureLimitQuery</c>).</item>
/// </list>
/// Une fonctionnalité absente d'un plan est DÉSACTIVÉE (domaine) ; un restaurant sans abonnement vivant n'a droit à rien.
/// </summary>
public static class BillingFeatures
{
    /// <summary>Consommation : notifications par mois et par canal (comptées par le module Notifications).</summary>
    public const string NotificationsEmailMonthly = "notifications.email.monthly";
    public const string NotificationsSmsMonthly = "notifications.sms.monthly";
    public const string NotificationsWhatsAppMonthly = "notifications.whatsapp.monthly";
    public const string NotificationsPushMonthly = "notifications.push.monthly";
    public const string NotificationsInAppMonthly = "notifications.inapp.monthly";

    /// <summary>Consommation : commandes passées par mois.</summary>
    public const string OrdersMonthly = "orders.monthly";

    /// <summary>Stock : établissements, membres du personnel.</summary>
    public const string Locations = "locations.count";
    public const string StaffMembers = "staff.count";

    /// <summary>Vente en ligne (options) : application en marque blanche, bornes (limite = nombre), API (site du restaurant et clés serveur).</summary>
    public const string StorefrontMobileApp = "storefront.mobile_app";
    public const string StorefrontKiosk = "storefront.kiosk";
    public const string StorefrontApi = PlanFeatures.IntegrationsApi;

    /// <summary>Le plan permet d'acheter des établissements supplémentaires (option).</summary>
    public const string LocationsExtra = "locations.extra";

    // Fonctionnalités contrôlées à l'usage par les modules (voir PlanFeatures).
    public const string CustomersAdvanced = PlanFeatures.CustomersAdvanced;
    public const string Loyalty = PlanFeatures.Loyalty;
    public const string MarketingCampaigns = PlanFeatures.MarketingCampaigns;
    public const string MarketingGiftCards = PlanFeatures.MarketingGiftCards;
    public const string Automations = PlanFeatures.Automations;
    public const string ReportingAdvanced = PlanFeatures.ReportingAdvanced;
    public const string ReportingConsolidated = PlanFeatures.ReportingConsolidated;
    public const string IntegrationsWebhooks = PlanFeatures.IntegrationsWebhooks;
    public const string NotificationsOwnAccount = PlanFeatures.NotificationsOwnAccount;
    public const string SupportPriority = PlanFeatures.SupportPriority;

    /// <summary>Tous les codes connus, dans l'ordre d'affichage de la console.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Locations, LocationsExtra, StaffMembers, OrdersMonthly,
        NotificationsEmailMonthly, NotificationsSmsMonthly, NotificationsWhatsAppMonthly, NotificationsPushMonthly, NotificationsInAppMonthly,
        NotificationsOwnAccount, Loyalty, CustomersAdvanced, MarketingCampaigns, MarketingGiftCards, Automations,
        ReportingAdvanced, ReportingConsolidated, StorefrontMobileApp, StorefrontKiosk, StorefrontApi, IntegrationsWebhooks,
        SupportPriority,
    ];

    /// <summary>Le code de la fonctionnalité « notifications du mois » d'un canal (nom de l'enum en minuscules).</summary>
    public static string NotificationsMonthly(string channel) =>
        "notifications." + channel.Trim().ToLowerInvariant() + ".monthly";

    /// <summary>La période de consommation qui contient <paramref name="instant"/> : le mois civil UTC.</summary>
    public static Period ConsumptionPeriod(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return Period.Create(start, start.AddMonths(1));
    }
}

/// <summary>Périodes facturées d'un plan.</summary>
public static class BillingPeriods
{
    /// <summary>La période d'un intervalle de facturation commençant à <paramref name="start"/>.</summary>
    public static Period Starting(DateTimeOffset start, BillingInterval interval) =>
        Period.Create(start, interval == BillingInterval.Yearly ? start.AddYears(1) : start.AddMonths(1));
}

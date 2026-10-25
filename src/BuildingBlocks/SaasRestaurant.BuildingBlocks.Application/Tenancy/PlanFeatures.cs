namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Fonctionnalités réservées à certains plans (catalogue Billing). Chaque module les vérifie au moment de l'usage par
/// <see cref="IPlanFeatureGate"/> ; un refus lève <see cref="Required"/> (403 <c>common.plan.feature_required</c>).
/// </summary>
public static class PlanFeatures
{
    // Clients et marketing.
    public const string CustomersAdvanced = "customers.advanced";
    public const string Loyalty = "loyalty";
    public const string MarketingCampaigns = "marketing.campaigns";
    public const string MarketingGiftCards = "marketing.gift_cards";
    public const string Automations = "automations";

    // Pilotage.
    public const string ReportingAdvanced = "reporting.advanced";
    public const string ReportingConsolidated = "reporting.consolidated";

    // Développeurs.
    public const string IntegrationsApi = "storefront.api"; // code historique de l'option API
    public const string IntegrationsWebhooks = "integrations.webhooks";

    // Envoi avec le compte du restaurant (email, SMS, WhatsApp, push).
    public const string NotificationsOwnAccount = "notifications.byok";

    /// <summary>Support prioritaire : information pour la console et le back-office, sans contrôle technique.</summary>
    public const string SupportPriority = "support.priority";

    public const string RequiredCode = "common.plan.feature_required";
    public const string OverLimitCode = "common.plan.over_limit";

    /// <summary>Refus : la fonctionnalité n'est pas dans le plan (ni dans une option) du restaurant.</summary>
    public static ForbiddenException Required(string feature) =>
        new(new DomainError(RequiredCode, $"This feature is not included in your plan ({feature})."));

    /// <summary>Refus : élément gelé, au-delà de la limite du plan (après un passage à un plan inférieur).</summary>
    public static ForbiddenException OverLimit(string what) =>
        new(new DomainError(OverLimitCode, $"Frozen: beyond the limit of your plan ({what})."));
}

/// <summary>
/// Le plan du restaurant permet-il une fonctionnalité ? Implémenté par l'hôte (Billing). Fail-closed : un tenant autre
/// que celui du contexte, ou sans abonnement vivant, n'a droit à rien.
/// </summary>
public interface IPlanFeatureGate
{
    Task<bool> AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken = default);
}

public static class PlanFeatureGateExtensions
{
    /// <summary>Lève 403 <c>common.plan.feature_required</c> si la fonctionnalité n'est pas permise.</summary>
    public static async Task RequireAsync(this IPlanFeatureGate gate, TenantId tenantId, string feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if (!await gate.AllowsAsync(tenantId, feature, cancellationToken))
        {
            throw PlanFeatures.Required(feature);
        }
    }
}

/// <summary>Sans hôte branché (tests d'un module seul) : tout est permis.</summary>
public sealed class AllowAllPlanFeatures : IPlanFeatureGate
{
    public Task<bool> AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

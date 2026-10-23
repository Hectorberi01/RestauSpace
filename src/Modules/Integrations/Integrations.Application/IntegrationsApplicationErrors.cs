using Integrations.Domain;

namespace Integrations.Application;

/// <summary>Erreurs des cas d'usage d'Integrations (les erreurs des règles métier sont dans Integrations.Domain).</summary>
public static class IntegrationsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("integrations.concurrent_update", "The resource was changed by someone else; reload it and try again.", DomainErrorKind.Conflict);

    public static readonly DomainError TenantNotWritable =
        new("integrations.tenant.not_writable", "The restaurant account is suspended or closed: ask the platform to restore it.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError EndpointNotFound = new("integrations.webhook_endpoint.not_found", "The webhook endpoint does not exist.");
    public static readonly DomainError DeliveryNotFound = new("integrations.webhook_delivery.not_found", "The webhook delivery does not exist.");
    public static readonly DomainError CredentialNotFound = new("integrations.api_credential.not_found", "The API credential does not exist.");

    public static readonly DomainError EventTypeNotPublished =
        new("integrations.event_type.not_published", "This event type is not published to webhooks.");
    public static readonly DomainError NotDeadLettered =
        new("integrations.webhook_delivery.not_dead_lettered", "Only an abandoned delivery can be sent again.");
    // Une clé ne reçoit que des droits que son auteur détient lui-même.
    public static readonly DomainError ScopeNotHeld =
        new("integrations.api_credential.scope_not_held", "You can only grant a key permissions that you hold yourself.");
    public static readonly DomainError ApiOptionRequired =
        new("integrations.api_credential.option_required", "API keys are an option of your subscription: add it first.");
    public static readonly DomainError PageInvalid = new("integrations.page_invalid", "Skip must be zero or more and take between 1 and 100.");
}

/// <summary>Codes de permission déclarés par le module Integrations pour les restaurants (accordés via les rôles Staff).</summary>
public static class IntegrationsPermissions
{
    /// <summary>Voir les endpoints, les livraisons et les clés d'API (jamais un secret ni une clé).</summary>
    public const string View = "integrations.view";

    /// <summary>Gérer les endpoints de webhooks et les clés d'API.</summary>
    public const string Manage = "integrations.manage";

    public static IReadOnlyList<string> All { get; } = [View, Manage];
}

/// <summary>Réglages des cas d'usage (section <c>Integrations</c> de l'hôte).</summary>
public sealed class IntegrationsSettings
{
    /// <summary>Les scopes qu'une clé d'API peut recevoir : des codes de permission ouverts aux intégrations (fournis par l'hôte).</summary>
    public IList<string> ApiScopes { get; } = new List<string>();

    /// <summary>Politique de retry figée sur chaque nouvelle livraison.</summary>
    public WebhookRetryPolicy RetryPolicy { get; set; } = WebhookRetryPolicy.Default;

    /// <summary>Une tentative ouverte depuis plus longtemps est close comme « timeout » (worker perdu).</summary>
    public TimeSpan StuckAttemptAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Un endpoint est désactivé par le système après autant de livraisons abandonnées d'affilée.</summary>
    public int AutoDisableAfterDeadLetters { get; set; } = 5;

    /// <summary>Après une rotation (non immédiate), l'ancien secret signe encore pendant cette durée.</summary>
    public TimeSpan SecretGracePeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Une clé utilisable mais inutilisée depuis cette durée est signalée comme dormante.</summary>
    public TimeSpan DormantKeyAfter { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Précision de la date de dernière utilisation d'une clé (au plus une écriture par clé et par période).</summary>
    public TimeSpan KeyUsageResolution { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Le catalogue des scopes, construit à la demande depuis <see cref="ApiScopes"/>.</summary>
    public ApiScopeCatalog ScopeCatalog() => ApiScopeCatalog.Create(ApiScopes.Select(ApiScope.Create));
}

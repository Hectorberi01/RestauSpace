namespace Integrations.Domain;

// Événements des endpoints. Jamais un secret ni une référence de secret ; l'URL seulement sous sa forme masquée
// (WebhookUrl.Redacted : le chemin et la requête peuvent porter un jeton du destinataire). Actor : l'auteur, pour l'audit
// (null quand l'appelant ne l'a pas fourni).

/// <param name="RedactedUrl">Schéma, hôte et port ; chemin et requête masqués.</param>
public sealed record WebhookEndpointCreated(
    TenantId TenantId,
    WebhookEndpointId EndpointId,
    string RedactedUrl,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);

public sealed record WebhookEndpointDisabled(
    TenantId TenantId,
    WebhookEndpointId EndpointId,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);

/// <summary>Désactivé par le système après trop de livraisons abandonnées d'affilée (le restaurant est prévenu).</summary>
public sealed record WebhookEndpointAutoDisabled(
    TenantId TenantId,
    WebhookEndpointId EndpointId,
    string RedactedUrl,
    int ConsecutiveDeadLetters,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WebhookEndpointEnabled(
    TenantId TenantId,
    WebhookEndpointId EndpointId,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Sans valeur de référence : seul le fait compte pour les abonnés (audit, nettoyage des secrets).
/// <paramref name="PreviousValidUntilUtc"/> : fin de la période de grâce de l'ancien secret (null : révoqué aussitôt).
/// </summary>
public sealed record WebhookSecretReferenceRotated(
    TenantId TenantId,
    WebhookEndpointId EndpointId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? PreviousValidUntilUtc = null,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);

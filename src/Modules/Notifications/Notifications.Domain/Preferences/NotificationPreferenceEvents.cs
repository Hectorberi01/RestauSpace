namespace Notifications.Domain;

// Faits des préférences : ids, finalité, canal et code de source. Aucune adresse, aucun texte libre.

public sealed record NotificationOptedOut(
    TenantId TenantId,
    NotificationPreferenceId PreferenceId,
    RecipientKind OwnerKind,
    Guid OwnerId,
    NotificationPurpose Purpose,
    NotificationChannel Channel,
    string Source,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationOptedIn(
    TenantId TenantId,
    NotificationPreferenceId PreferenceId,
    RecipientKind OwnerKind,
    Guid OwnerId,
    NotificationPurpose Purpose,
    NotificationChannel Channel,
    string Source,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Un utilisateur coupe ou rétablit un type d'alerte d'équipe : la clé du type, jamais le contenu.</summary>
public sealed record NotificationTypeMuteChanged(
    TenantId TenantId,
    NotificationPreferenceId PreferenceId,
    Guid OwnerId,
    string Type,
    bool Muted,
    string Source,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

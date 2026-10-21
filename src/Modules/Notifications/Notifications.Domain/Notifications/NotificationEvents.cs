namespace Notifications.Domain;

// TenantId est null pour une notification de niveau plateforme. Les événements ne portent que des ids et des codes :
// ni adresse, ni contenu, ni message du fournisseur, donc rien à y effacer quand une notification est expurgée.

public sealed record NotificationCreated(
    TenantId? TenantId,
    NotificationId NotificationId,
    NotificationChannel Channel,
    string TemplateKey,
    NotificationPurpose Purpose,
    string Provider,
    NotificationPriority Priority,
    DateTimeOffset? ScheduledAtUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationScheduled(
    TenantId? TenantId, NotificationId NotificationId, DateTimeOffset ScheduledAtUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Le prestataire de la tentative a accepté le message. <c>Provider</c> : son code (une relance peut changer de prestataire).</summary>
public sealed record NotificationSent(
    TenantId? TenantId, NotificationId NotificationId, NotificationChannel Channel, string Provider, int AttemptNumber, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationPartiallyDelivered(
    TenantId? TenantId, NotificationId NotificationId, NotificationChannel Channel, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationDelivered(
    TenantId? TenantId, NotificationId NotificationId, NotificationChannel Channel, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Une tentative a échoué. Levé pour chaque tentative en échec, relancée ou non. Seul le CODE de l'échec y figure : le
/// message brut du prestataire reste sur l'agrégat (<see cref="Notification.LastFailure"/>), il peut citer l'adresse.
/// </summary>
public sealed record NotificationFailed(
    TenantId? TenantId,
    NotificationId NotificationId,
    NotificationChannel Channel,
    int AttemptNumber,
    string FailureCode,
    NotificationFailureKind FailureKind,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Le message ne sera pas livré (« dead letter ») : échec permanent, ou dernière tentative permise par la politique de
/// relance en échec.
/// </summary>
public sealed record NotificationDeliveryAbandoned(
    TenantId? TenantId, NotificationId NotificationId, NotificationChannel Channel, int AttemptCount, string FailureCode, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationRetryScheduled(
    TenantId? TenantId, NotificationId NotificationId, int NextAttemptNumber, DateTimeOffset NextAttemptAtUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationCancelled(TenantId? TenantId, NotificationId NotificationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationRedacted(TenantId? TenantId, NotificationId NotificationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

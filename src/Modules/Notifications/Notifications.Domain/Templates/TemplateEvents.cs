namespace Notifications.Domain;

/// <param name="TenantId">Null pour un modèle de niveau plateforme.</param>
public sealed record TemplatePublished(
    TenantId? TenantId,
    TemplateId TemplateId,
    string Key,
    NotificationChannel Channel,
    string Language,
    int Version,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <param name="TenantId">Null pour un modèle de niveau plateforme.</param>
public sealed record TemplateArchived(
    TenantId? TenantId,
    TemplateId TemplateId,
    string Key,
    NotificationChannel Channel,
    string Language,
    int Version,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <param name="TenantId">Null pour un modèle de niveau plateforme.</param>
public sealed record TemplateEnabled(
    TenantId? TenantId, TemplateId TemplateId, string Key, NotificationChannel Channel, string Language, int Version, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Le template ne sert plus : pour un restaurant, ses notifications reprennent le template de la plateforme.</summary>
/// <param name="TenantId">Null pour un modèle de niveau plateforme.</param>
public sealed record TemplateDisabled(
    TenantId? TenantId, TemplateId TemplateId, string Key, NotificationChannel Channel, string Language, int Version, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

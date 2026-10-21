namespace Notifications.Domain;

// Faits de la configuration d'un restaurant. Aucun ne porte de secret (au plus le code du prestataire).

public sealed record NotificationConfigurationCreated(
    TenantId TenantId,
    RestaurantNotificationConfigurationId ConfigurationId,
    NotificationProviderId ProviderId,
    NotificationProviderCode Provider,
    NotificationChannel Channel,
    NotificationProviderMode Mode,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Les identifiants du compte du restaurant viennent d'être rangés au coffre (première saisie ou rotation).</summary>
public sealed record NotificationConfigurationConfigured(
    TenantId TenantId, RestaurantNotificationConfigurationId ConfigurationId, NotificationProviderCode Provider, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationConfigurationModeChanged(
    TenantId TenantId,
    RestaurantNotificationConfigurationId ConfigurationId,
    NotificationProviderCode Provider,
    NotificationProviderMode Mode,
    NotificationConfigurationStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationConfigurationProviderChanged(
    TenantId TenantId,
    RestaurantNotificationConfigurationId ConfigurationId,
    NotificationProviderCode PreviousProvider,
    NotificationProviderId ProviderId,
    NotificationProviderCode Provider,
    NotificationConfigurationStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationConfigurationEnabled(
    TenantId TenantId, RestaurantNotificationConfigurationId ConfigurationId, NotificationProviderCode Provider, NotificationChannel Channel, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationConfigurationDisabled(
    TenantId TenantId, RestaurantNotificationConfigurationId ConfigurationId, NotificationProviderCode Provider, NotificationChannel Channel, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record NotificationChannelAssignedToLocation(
    TenantId TenantId,
    RestaurantNotificationConfigurationId ConfigurationId,
    NotificationChannel Channel,
    LocationId LocationId,
    int Priority,
    bool IsEnabled,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationChannelRemovedFromLocation(
    TenantId TenantId, RestaurantNotificationConfigurationId ConfigurationId, NotificationChannel Channel, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

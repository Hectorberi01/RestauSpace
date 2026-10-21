namespace Notifications.Domain;

// Faits du catalogue de la plateforme. Aucun ne porte de secret : le prestataire n'en contient aucun (les identifiants
// sont rangés au coffre, hors agrégat).

public sealed record NotificationProviderCreated(
    NotificationProviderId ProviderId,
    NotificationProviderCode Code,
    NotificationChannel Channel,
    NotificationProviderConfigurationType ConfigurationType,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationProviderRenamed(
    NotificationProviderId ProviderId, NotificationProviderCode Code, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationProviderCapabilitiesUpdated(
    NotificationProviderId ProviderId, NotificationProviderCode Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationProviderConfigurationTypeChanged(
    NotificationProviderId ProviderId,
    NotificationProviderCode Code,
    NotificationProviderConfigurationType ConfigurationType,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationProviderEnabled(
    NotificationProviderId ProviderId, NotificationProviderCode Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record NotificationProviderDisabled(
    NotificationProviderId ProviderId, NotificationProviderCode Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

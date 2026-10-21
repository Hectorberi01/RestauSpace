namespace Delivery.Domain;

public sealed record DeliverySettingsCreated(
    TenantId TenantId,
    DeliverySettingsId SettingsId,
    LocationId LocationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Retrait ou livraison activés, désactivés, ou délais modifiés.</summary>
public sealed record DeliveryServicesChanged(
    TenantId TenantId,
    DeliverySettingsId SettingsId,
    LocationId LocationId,
    bool PickupEnabled,
    bool DeliveryEnabled,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Une zone ajoutée, modifiée, activée, désactivée ou retirée.</summary>
public sealed record DeliveryZonesChanged(
    TenantId TenantId,
    DeliverySettingsId SettingsId,
    LocationId LocationId,
    int ActiveZones,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

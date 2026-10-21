namespace Payments.Domain;

// Faits de la configuration d'un restaurant. Aucun ne porte de secret (au plus la référence du coffre, absente ici).

public sealed record PaymentConfigurationCreated(
    TenantId TenantId,
    RestaurantPaymentConfigurationId ConfigurationId,
    PaymentProviderId ProviderId,
    PaymentProviderCode Provider,
    PaymentMode Mode,
    bool IsEnabled,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationConfigured(
    TenantId TenantId, RestaurantPaymentConfigurationId ConfigurationId, PaymentProviderCode Provider, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationModeChanged(
    TenantId TenantId,
    RestaurantPaymentConfigurationId ConfigurationId,
    PaymentProviderCode Provider,
    PaymentMode Mode,
    PaymentConfigurationStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationEnabled(
    TenantId TenantId, RestaurantPaymentConfigurationId ConfigurationId, PaymentProviderCode Provider, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationDisabled(
    TenantId TenantId, RestaurantPaymentConfigurationId ConfigurationId, PaymentProviderCode Provider, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationMethodActivated(
    TenantId TenantId, RestaurantPaymentConfigurationId ConfigurationId, PaymentProviderCode Provider, PaymentMethodType Method, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentConfigurationMethodDeactivated(
    TenantId TenantId, RestaurantPaymentConfigurationId ConfigurationId, PaymentProviderCode Provider, PaymentMethodType Method, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentMethodAssignedToLocation(
    TenantId TenantId,
    RestaurantPaymentConfigurationId ConfigurationId,
    PaymentProviderCode Provider,
    LocationId LocationId,
    PaymentMethodType Method,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary><see cref="Method"/> null : tous les moyens de la configuration ont été retirés de l'établissement.</summary>
public sealed record PaymentMethodRemovedFromLocation(
    TenantId TenantId,
    RestaurantPaymentConfigurationId ConfigurationId,
    PaymentProviderCode Provider,
    LocationId LocationId,
    PaymentMethodType? Method,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

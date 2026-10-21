namespace Integrations.Domain;

public sealed record WebhookDelivered(
    TenantId TenantId,
    DeliveryId DeliveryId,
    WebhookEndpointId EndpointId,
    EventId EventId,
    int AttemptCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Une tentative a échoué et une autre est planifiée.</summary>
public sealed record WebhookDeliveryFailed(
    TenantId TenantId,
    DeliveryId DeliveryId,
    WebhookEndpointId EndpointId,
    int AttemptCount,
    string Reason,
    DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WebhookDeadLettered(
    TenantId TenantId,
    DeliveryId DeliveryId,
    WebhookEndpointId EndpointId,
    EventId EventId,
    int AttemptCount,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

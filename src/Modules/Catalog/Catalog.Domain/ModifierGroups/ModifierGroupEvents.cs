namespace Catalog.Domain;

public sealed record ModifierAdded(
    TenantId TenantId,
    ModifierGroupId ModifierGroupId,
    ModifierId ModifierId,
    string Name,
    Money PriceDelta,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ModifierRemoved(TenantId TenantId, ModifierGroupId ModifierGroupId, ModifierId ModifierId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ModifierPriceChanged(
    TenantId TenantId,
    ModifierGroupId ModifierGroupId,
    ModifierId ModifierId,
    Money PreviousPriceDelta,
    Money NewPriceDelta,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ModifierAvailabilityChanged(
    TenantId TenantId,
    ModifierGroupId ModifierGroupId,
    ModifierId ModifierId,
    bool IsAvailable,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

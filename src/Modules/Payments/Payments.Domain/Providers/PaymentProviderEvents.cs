namespace Payments.Domain;

// Faits du catalogue de la plateforme. Aucun ne porte de secret.

public sealed record PaymentProviderRegistered(PaymentProviderId ProviderId, PaymentProviderCode Code, PaymentProviderType Type, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentProviderRenamed(PaymentProviderId ProviderId, PaymentProviderCode Code, string DisplayName, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentProviderCapabilitiesChanged(PaymentProviderId ProviderId, PaymentProviderCode Code, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentProviderEnabled(PaymentProviderId ProviderId, PaymentProviderCode Code, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record PaymentProviderDisabled(PaymentProviderId ProviderId, PaymentProviderCode Code, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

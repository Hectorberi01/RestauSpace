namespace Tenants.Domain.Tenants;

public sealed record TenantCreated(
    TenantId TenantId,
    string Name,
    string DefaultCurrencyCode,
    string TimeZoneId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantActivated(TenantId TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantSuspended(
    TenantId TenantId,
    TenantSuspensionReason Reason,
    TenantStatus PreviousStatus,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantReactivated(TenantId TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantClosed(TenantId TenantId, TenantStatus PreviousStatus, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantRenamed(TenantId TenantId, string PreviousName, string NewName, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantDefaultCurrencyChanged(
    TenantId TenantId,
    string PreviousCurrencyCode,
    string NewCurrencyCode,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantTimeZoneChanged(
    TenantId TenantId,
    string PreviousTimeZoneId,
    string NewTimeZoneId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantPriceTaxModeChanged(
    TenantId TenantId,
    bool PricesIncludeTax,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantBrandingUpdated(TenantId TenantId, Branding Branding, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La présentation publique a changé ; son contenu ne voyage pas dans l'événement.</summary>
public sealed record TenantPublicProfileUpdated(TenantId TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

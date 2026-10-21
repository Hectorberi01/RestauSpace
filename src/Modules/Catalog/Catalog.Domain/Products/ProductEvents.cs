namespace Catalog.Domain;

public sealed record ProductCreated(TenantId TenantId, ProductId ProductId, string Name, Money BasePrice, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ProductPriceChanged(TenantId TenantId, ProductId ProductId, Money PreviousBasePrice, Money NewBasePrice, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ProductActivated(TenantId TenantId, ProductId ProductId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Le produit a été marqué indisponible (nom imposé par la spécification métier).</summary>
public sealed record ProductUnavailable(TenantId TenantId, ProductId ProductId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ProductArchived(TenantId TenantId, ProductId ProductId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Le prix effectif d'une variante a changé (prix de base inchangé). <c>NewPrice</c> = prix de base + delta.</summary>
public sealed record ProductVariantPriceChanged(
    TenantId TenantId,
    ProductId ProductId,
    VariantId VariantId,
    Money PreviousPriceDelta,
    Money NewPriceDelta,
    Money NewPrice,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ProductVariantAvailabilityChanged(
    TenantId TenantId,
    ProductId ProductId,
    VariantId VariantId,
    bool IsAvailable,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Un prix propre à un établissement ou à un canal est posé (<c>BasePrice</c>) ou retiré (null) ; le prix de base du
/// produit ne change pas.
/// </summary>
public sealed record ProductPriceOverrideChanged(
    TenantId TenantId,
    ProductId ProductId,
    LocationId? LocationId,
    PriceChannel? Channel,
    Money? BasePrice,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Les établissements où le produit est vendu ont changé (liste vide : tous).</summary>
public sealed record ProductLocationsChanged(TenantId TenantId, ProductId ProductId, IReadOnlyList<Guid> LocationIds, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ProductAvailabilityAtLocationChanged(
    TenantId TenantId, ProductId ProductId, LocationId LocationId, bool IsAvailable, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

namespace Catalog.Domain;

/// <summary>
/// Un produit, un groupe d'options ou un menu du tenant a changé : les applications de vente rechargent le catalogue
/// (leur ETag ne correspond plus). Dérivé à l'enregistrement, une fois par tenant et par transaction.
/// </summary>
public sealed record CatalogChanged(TenantId TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

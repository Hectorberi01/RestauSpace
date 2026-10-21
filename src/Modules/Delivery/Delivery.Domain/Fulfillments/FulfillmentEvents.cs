namespace Delivery.Domain;

// Ids, mode et statut seulement : jamais le nom, le téléphone ni l'adresse du client ou du livreur.

/// <param name="DeliveryFee">Frais de la zone figés sur une livraison ; null pour un retrait.</param>
public sealed record FulfillmentCreated(
    TenantId TenantId,
    FulfillmentId FulfillmentId,
    OrderId OrderId,
    LocationId LocationId,
    FulfillmentMode Mode,
    Money? DeliveryFee,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Le livreur a encaissé le client à la livraison : le paiement est enregistré sur la commande (hôte). Montant et moyen
/// seulement : ni client ni livreur.
/// </summary>
public sealed record DeliveryPaymentCollected(
    TenantId TenantId,
    FulfillmentId FulfillmentId,
    OrderId OrderId,
    LocationId LocationId,
    Money Amount,
    CollectionMethod Method,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FulfillmentStatusChanged(
    TenantId TenantId,
    FulfillmentId FulfillmentId,
    OrderId OrderId,
    LocationId LocationId,
    FulfillmentMode Mode,
    FulfillmentStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

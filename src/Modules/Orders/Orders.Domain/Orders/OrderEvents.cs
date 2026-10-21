namespace Orders.Domain;

public sealed record OrderCreated(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    string OrderNumber,
    OrderType OrderType,
    string CurrencyCode,
    PriceTaxMode PriceTaxMode,
    CustomerId? CustomerId,
    TableSessionId? TableSessionId,
    DateTimeOffset OccurredAtUtc,
    OrderChannel Channel = OrderChannel.Counter,
    DateTimeOffset? ScheduledForUtc = null) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Porte les totaux figés à la validation : ils ne peuvent plus changer ensuite. <paramref name="PriceTaxMode"/>
/// indique comment les lire : TaxTotal s'ajoute à SubTotal - DiscountTotal (HT) ou y est contenu
/// (TTC) ; Total est le montant à payer dans les deux modes.
/// </summary>
public sealed record OrderPlaced(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    string OrderNumber,
    OrderType OrderType,
    PriceTaxMode PriceTaxMode,
    Money SubTotal,
    Money DiscountTotal,
    Money TaxTotal,
    Money Total,
    CustomerId? CustomerId,
    TableSessionId? TableSessionId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record OrderAccepted(TenantId TenantId, OrderId OrderId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Fait déclenchant la consommation de stock des lignes de la commande, dans <paramref name="LocationId"/>.</summary>
public sealed record OrderPreparationStarted(TenantId TenantId, OrderId OrderId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record OrderReady(TenantId TenantId, OrderId OrderId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record OrderCompleted(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    PriceTaxMode PriceTaxMode,
    Money Total,
    CustomerId? CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>(source, référence) d'une remise, telle que portée par <see cref="OrderCancelled"/>.</summary>
public sealed record DiscountOrigin(DiscountSource Source, string Reference);

/// <summary>
/// <paramref name="PreviousStatus"/> indique aux consommateurs jusqu'où la commande était allée (cuisine à arrêter,
/// paiement à rembourser). <paramref name="Discounts"/> et <paramref name="CustomerId"/> leur permettent d'annuler leurs
/// effets de bord (utilisation de coupon, récompense de fidélité) sans recharger la commande.
/// </summary>
public sealed record OrderCancelled(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    OrderStatus PreviousStatus,
    string Reason,
    Money Total,
    CustomerId? CustomerId,
    IReadOnlyList<DiscountOrigin> Discounts,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La commande a été rattachée à une autre fiche client, ou détachée (<paramref name="CustomerId"/> null).</summary>
public sealed record OrderCustomerChanged(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    CustomerId? PreviousCustomerId,
    CustomerId? CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Frais de livraison posés, remplacés ou retirés (<paramref name="DeliveryFee"/> null) ; <paramref name="Total"/> : le nouveau montant à payer.</summary>
public sealed record OrderDeliveryFeeChanged(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    Money? DeliveryFee,
    Money Total,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La commande sur place change de table (transfert ou fusion de tables).</summary>
public sealed record OrderTableSessionChanged(
    TenantId TenantId,
    OrderId OrderId,
    LocationId LocationId,
    TableSessionId? PreviousTableSessionId,
    TableSessionId TableSessionId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

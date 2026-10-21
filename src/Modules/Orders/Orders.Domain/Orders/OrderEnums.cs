namespace Orders.Domain;

public enum OrderType
{
    DineIn = 1,
    Takeaway = 2,
    Delivery = 3,
}

/// <summary>
/// Par où la commande est arrivée : caisse (personnel), QR code de table, site web, application mobile, borne, ou API
/// d'un partenaire. Posé par le serveur selon l'appelant, jamais choisi par le client.
/// </summary>
public enum OrderChannel
{
    Counter = 1,
    QrCode = 2,
    Web = 3,
    MobileApp = 4,
    Kiosk = 5,
    Api = 6,

    /// <summary>Prise par l'équipe au téléphone.</summary>
    Phone = 7,

    /// <summary>Reçue par message WhatsApp et saisie par l'équipe.</summary>
    WhatsApp = 8,

    /// <summary>Reçue par une plateforme de livraison (Glovo, Gozem, Yango...) : saisie par l'équipe ou poussée par un intégrateur.</summary>
    DeliveryPlatform = 9,
}

/// <summary>
/// D'où vient la commande : le canal et, pour la vente en ligne, l'application de vente du restaurant ; pour une
/// plateforme de livraison, son code (<c>glovo</c>, <c>gozem</c>...) et le numéro de la commande chez elle.
/// </summary>
public sealed record OrderOrigin(OrderChannel Channel, Guid? SourceAppId = null, string? Platform = null, string? ExternalReference = null)
{
    public const int MaxPlatformLength = 30;
    public const int MaxExternalReferenceLength = 64;

    public static OrderOrigin Counter { get; } = new(OrderChannel.Counter);

    public static OrderOrigin FromPlatform(string? platform, string? externalReference) =>
        new(OrderChannel.DeliveryPlatform, null, platform?.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim());
}

public enum OrderStatus
{
    Draft = 1,
    Placed = 2,
    Accepted = 3,
    Preparing = 4,
    Ready = 5,
    Completed = 6,
    Cancelled = 7,
}

/// <summary>
/// Indique si les prix fournis à une commande (prix unitaires, deltas d'options, remises) incluent déjà la taxe.
/// Réglage du tenant, lu par l'Application et figé sur la commande à sa création.
/// </summary>
public enum PriceTaxMode
{
    TaxInclusive = 1,
    TaxExclusive = 2,
}

/// <summary>Qui a décidé la remise. La décision elle-même (éligibilité, montant) est prise hors de ce module.</summary>
public enum DiscountSource
{
    Promotion = 1,
    Coupon = 2,
    LoyaltyReward = 3,
    Manual = 4,
}

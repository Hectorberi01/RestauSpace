namespace Delivery.Domain;

public enum FulfillmentMode
{
    /// <summary>Le client vient chercher sa commande (à emporter).</summary>
    Pickup = 1,

    /// <summary>La commande est livrée à une adresse.</summary>
    Delivery = 2,
}

/// <summary>
/// Retrait : Pending -> ReadyForPickup -> Completed. Livraison : Pending -> Assigned -> OutForDelivery -> Completed, ou
/// Failed (client absent...) puis retour à Pending. Cancelled depuis tout état non terminé.
/// </summary>
public enum FulfillmentStatus
{
    Pending = 1,
    ReadyForPickup = 2,
    Assigned = 3,
    OutForDelivery = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

/// <summary>Comment le client a réglé le livreur : espèces (gardées par le livreur jusqu'à la remise) ou mobile money sur le téléphone du restaurant.</summary>
public enum CollectionMethod
{
    Cash = 1,
    MobileMoney = 2,
}

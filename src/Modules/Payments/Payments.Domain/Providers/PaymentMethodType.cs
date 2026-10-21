namespace Payments.Domain;

/// <summary>
/// Moyen de paiement tel que le client le voit, indépendant du prestataire qui le traite : plusieurs prestataires
/// peuvent offrir <see cref="MobileMoney"/> (MTN MoMo, Moov Money), un prestataire peut en offrir plusieurs (Stripe :
/// carte, Apple Pay, Google Pay). Valeurs stables (stockées).
/// </summary>
public enum PaymentMethodType
{
    /// <summary>Carte saisie en ligne (page du prestataire : jamais de numéro de carte chez nous).</summary>
    Card = 1,
    ApplePay = 2,
    GooglePay = 3,
    PayPal = 4,

    /// <summary>Mobile money initié par le prestataire (demande de paiement sur le téléphone du client).</summary>
    MobileMoney = 5,
    BankTransfer = 6,

    /// <summary>Espèces au comptoir ou à la livraison.</summary>
    Cash = 20,

    /// <summary>Carte sur un terminal de paiement (TPE) sur place.</summary>
    CardOnSite = 21,

    /// <summary>Mobile money reçu sur le téléphone du restaurant et vérifié par le caissier.</summary>
    MobileMoneyOnSite = 22,
    MealVoucher = 23,

    /// <summary>Carte cadeau ou avoir du restaurant, débité au comptoir par son code.</summary>
    GiftCard = 24,

    /// <summary>Réglé par le client à la plateforme de livraison (Glovo, Gozem...), qui reverse au restaurant.</summary>
    DeliveryPlatform = 25,

    /// <summary>
    /// Espèces encaissées par le livreur du restaurant à la livraison : hors du tiroir jusqu'à la remise du livreur, qui
    /// les fait entrer dans la caisse (mouvement d'espèces).
    /// </summary>
    CashOnDelivery = 26,
}

public static class PaymentMethodTypes
{
    /// <summary>Encaissé sur place, sans prestataire en ligne.</summary>
    public static bool IsOnSiteOnly(this PaymentMethodType method) =>
        method is PaymentMethodType.Cash or PaymentMethodType.CardOnSite or PaymentMethodType.MobileMoneyOnSite or PaymentMethodType.MealVoucher
            or PaymentMethodType.GiftCard or PaymentMethodType.DeliveryPlatform or PaymentMethodType.CashOnDelivery;

    /// <summary>
    /// Compté à la clôture de caisse : encaissé sur place et reçu physiquement (ni carte cadeau, ni réglé à une plateforme,
    /// ni espèces encore chez le livreur : elles entrent dans la caisse à sa remise).
    /// </summary>
    public static bool IsCountedAtClose(this PaymentMethodType method) =>
        method.IsOnSiteOnly() && method is not (PaymentMethodType.GiftCard or PaymentMethodType.DeliveryPlatform or PaymentMethodType.CashOnDelivery);

    /// <summary>Ne passe que par un prestataire en ligne.</summary>
    public static bool IsOnlineOnly(this PaymentMethodType method) =>
        method is PaymentMethodType.Card or PaymentMethodType.ApplePay or PaymentMethodType.GooglePay
            or PaymentMethodType.PayPal or PaymentMethodType.MobileMoney;

    /// <summary>Ce moyen peut-il être offert par un prestataire de cette nature ? (Un virement : les deux.)</summary>
    public static bool FitsProviderType(this PaymentMethodType method, PaymentProviderType type) =>
        type == PaymentProviderType.OnSite ? !method.IsOnlineOnly() : !method.IsOnSiteOnly();
}

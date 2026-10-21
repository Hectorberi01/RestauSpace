using Payments.Domain;

namespace Payments.Application;

/// <summary>
/// Moyens sur place présents dans le catalogue de toute plateforme (posés à la migration s'ils manquent ; la plateforme
/// peut ensuite les renommer, changer leurs capacités, les retirer, et en ajouter d'autres).
/// </summary>
public static class BuiltInPaymentProviders
{
    /// <summary>Espèces encaissées par le livreur du restaurant à la livraison.</summary>
    public const string CourierCashCode = "courier_cash";

    /// <summary>Mobile money reçu sur le téléphone du restaurant (au comptoir ou à la livraison).</summary>
    public const string MobileMoneyCode = "mobile_money";

    public static IReadOnlyList<(PaymentProviderCode Code, string DisplayName, PaymentProviderCapability Capability)> OnSite { get; } =
    [
        (PaymentProviderCode.Cash, "Espèces", OnSiteCapability(PaymentMethodType.Cash)),
        (PaymentProviderCode.Create("card_terminal"), "Carte (terminal de paiement)", OnSiteCapability(PaymentMethodType.CardOnSite)),
        (PaymentProviderCode.Create(MobileMoneyCode), "Mobile money (téléphone du restaurant)", OnSiteCapability(PaymentMethodType.MobileMoneyOnSite)),
        (PaymentProviderCode.Create("bank_transfer"), "Virement bancaire", OnSiteCapability(PaymentMethodType.BankTransfer)),
        (PaymentProviderCode.Create(GiftCards.ProviderCode), "Carte cadeau ou avoir", OnSiteCapability(PaymentMethodType.GiftCard)),
        (PaymentProviderCode.Create("delivery_platform"), "Payé à la plateforme de livraison", OnSiteCapability(PaymentMethodType.DeliveryPlatform)),
        (PaymentProviderCode.Create(CourierCashCode), "Espèces encaissées par le livreur", OnSiteCapability(PaymentMethodType.CashOnDelivery)),
    ];

    /// <summary>
    /// Les prestataires en ligne intégrés dans le code, proposés d'office (la plateforme peut les retirer ou les modifier).
    /// Remboursement par API : Stripe, PayPal, Wave et Mollie ; les autres se remboursent depuis leur tableau de bord.
    /// </summary>
    public static IReadOnlyList<(PaymentProviderCode Code, string DisplayName, PaymentProviderCapability Capability)> Online { get; } =
    [
        (PaymentProviderCode.Create("fedapay"), "FedaPay", OnlineCapability(
            [PaymentMethodType.MobileMoney, PaymentMethodType.Card], ["BJ", "CI", "SN", "TG", "BF", "NE", "ML", "GN"], ["XOF", "GNF"], refund: false)),
        (PaymentProviderCode.Create("kkiapay"), "KKiaPay", OnlineCapability(
            [PaymentMethodType.MobileMoney, PaymentMethodType.Card], ["BJ", "CI", "SN", "TG", "BF"], ["XOF"], refund: false)),
        (PaymentProviderCode.Create("cinetpay"), "CinetPay", OnlineCapability(
            [PaymentMethodType.MobileMoney, PaymentMethodType.Card], ["BJ", "CI", "SN", "TG", "BF", "ML", "NE", "CM", "CD", "GN"], ["XOF", "XAF", "GNF", "CDF"], refund: false)),
        (PaymentProviderCode.Create("mtn_momo"), "MTN Mobile Money", OnlineCapability(
            [PaymentMethodType.MobileMoney], ["BJ", "CI", "CM", "CG", "GN", "GH", "UG", "ZM"], ["XOF", "XAF", "GNF", "GHS", "UGX", "ZMW", "EUR"], refund: false)),
        (PaymentProviderCode.Create("stripe"), "Stripe", OnlineCapability(
            [PaymentMethodType.Card, PaymentMethodType.ApplePay, PaymentMethodType.GooglePay], [], ["EUR", "USD", "GBP", "CHF", "CAD", "XOF"], refund: true)),
        (PaymentProviderCode.Create("paypal"), "PayPal", OnlineCapability(
            [PaymentMethodType.PayPal], [], ["EUR", "USD", "GBP", "CHF", "CAD"], refund: true)),

        // Wave rembourse par API, mais seulement la totalité d'un paiement.
        (PaymentProviderCode.Create("wave"), "Wave", OnlineCapability(
            [PaymentMethodType.MobileMoney], ["SN", "CI", "BF", "ML", "UG", "GM"], ["XOF", "UGX", "GMD"], refund: true, partialRefund: false)),
        (PaymentProviderCode.Create("flutterwave"), "Flutterwave", OnlineCapability(
            [PaymentMethodType.Card, PaymentMethodType.MobileMoney, PaymentMethodType.BankTransfer],
            ["NG", "GH", "KE", "ZA", "UG", "TZ", "RW", "ZM", "CI", "SN", "CM"],
            ["NGN", "GHS", "KES", "ZAR", "UGX", "TZS", "RWF", "ZMW", "XOF", "XAF", "USD", "EUR", "GBP"], refund: false)),
        (PaymentProviderCode.Create("paystack"), "Paystack", OnlineCapability(
            [PaymentMethodType.Card, PaymentMethodType.MobileMoney, PaymentMethodType.BankTransfer],
            ["NG", "GH", "ZA", "KE", "CI"], ["NGN", "GHS", "ZAR", "KES", "XOF", "USD"], refund: false)),
        (PaymentProviderCode.Create("paydunya"), "PayDunya", OnlineCapability(
            [PaymentMethodType.MobileMoney, PaymentMethodType.Card], ["SN", "CI", "BJ", "TG", "BF", "ML"], ["XOF"], refund: false)),
        (PaymentProviderCode.Create("mollie"), "Mollie", OnlineCapability(
            [PaymentMethodType.Card, PaymentMethodType.ApplePay, PaymentMethodType.PayPal, PaymentMethodType.BankTransfer], [],
            ["EUR", "GBP", "CHF", "USD", "PLN", "SEK", "DKK", "NOK"], refund: true)),
    ];

    private static PaymentProviderCapability OnlineCapability(
        PaymentMethodType[] methods, string[] countries, string[] currencies, bool refund, bool? partialRefund = null) =>
        PaymentProviderCapability.Create(
            methods,
            countries.Select(CountryCode.Create),
            currencies.Select(Currency.FromCode),
            supportsRefund: refund,
            supportsPartialRefund: partialRefund ?? refund,
            supportsWebhooks: true);

    // Tous pays, toutes devises ; un remboursement sur place est rendu de la main à la main, en tout ou partie.
    private static PaymentProviderCapability OnSiteCapability(PaymentMethodType method) =>
        PaymentProviderCapability.Create([method], supportsRefund: true, supportsPartialRefund: true);
}

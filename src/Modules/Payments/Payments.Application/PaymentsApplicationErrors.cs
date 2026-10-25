namespace Payments.Application;

/// <summary>Erreurs levées par les cas d'usage Payments (celles des règles métier sont dans Payments.Domain).</summary>
public static class PaymentsApplicationErrors
{
    public static readonly DomainError DepositNotFound = new("payments.deposit.not_found", "This reservation has no deposit.");
    public static readonly DomainError DepositNotPayable = new("payments.deposit.not_payable", "This deposit is not waiting for a payment.");
    public static readonly DomainError NoDepositToApply = new("payments.deposit.nothing_to_apply", "This reservation has no collected deposit left to deduct.");
    public static readonly DomainError LocationNotFound = new("payments.location.not_found", "The location does not exist in this restaurant.");
    public static readonly DomainError AmountCurrencyRequired = new("payments.assignment.currency_required", "A minimum or maximum amount needs its currency.");
    public static readonly DomainError PayerPhoneInvalid = new("payments.payer_phone.invalid", "The payer phone number is not valid (international format, 8 to 15 digits).");
    public static readonly DomainError ConcurrentUpdate =
        new("payments.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError PaymentNotFound = new("payments.payment.not_found", "The payment does not exist.");
    public static readonly DomainError OrderNotFound = new("payments.order.not_found", "The order does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("payments.tenant.not_writable", "The restaurant account is suspended or closed: its payments can be read, not taken.");

    public static readonly DomainError OrderNotPayable =
        new("payments.order.not_payable", "Only a placed order that is neither completed nor cancelled can be paid.");

    public static readonly DomainError NothingDue =
        new("payments.order.nothing_due", "Nothing is left to pay on this order (payments in progress included).");

    public static readonly DomainError MethodNotAccepted =
        new("payments.method.not_accepted", "The restaurant does not accept this payment method for this order (disabled, withdrawn by the platform, or not in this currency).");

    public static readonly DomainError GiftCardCodeRequired =
        new("payments.gift_card.code_required", "Enter the gift card or store credit code.");

    public static readonly DomainError GiftCardReferenceInvalid =
        new("payments.gift_card.reference_invalid", "This payment does not point to a gift card.");

    public static readonly DomainError StoreCreditNotForGiftCard =
        new("payments.gift_card.store_credit_not_allowed", "A gift card payment is refunded onto its card, not as a new store credit.");

    public static readonly DomainError PaymentMethodRequired =
        new("payments.method.payment_method_required", "This provider offers several payment methods: say which one is used.");

    public static readonly DomainError MethodTypeMismatch =
        new("payments.method.type_mismatch", "This payment method is not taken this way (counter versus online).");

    public static readonly DomainError PaymentConfigurationNotFound =
        new("payments.configuration.not_found", "The payment configuration does not exist.");

    public static readonly DomainError ProviderNotFound =
        new("payments.provider.not_found", "The payment provider does not exist.");

    public static readonly DomainError IntegrationMissing =
        new("payments.provider.integration_missing", "No integration exists in the platform code for this online provider.");

    public static readonly DomainError CredentialsInvalid =
        new("payments.configuration.credential_fields_invalid", "Every credential field of the provider is required, and only those.");

    public static readonly DomainError CredentialsUnreadable =
        new("payments.configuration.credentials_unreadable", "The merchant credentials of this payment provider can no longer be read; enter them again.");

    public static readonly DomainError TestNotApplicable =
        new("payments.configuration.test_not_applicable", "Only an online payment provider account can be tested.");

    public static readonly DomainError PlatformCollectionUnavailable =
        new("payments.platform_collection.unavailable", "Online payment of the subscription is not available: the platform's payment provider is not set up.");

    public static readonly DomainError GatewayFailed =
        new("payments.provider.call_failed", "The payment provider could not be reached or refused the request; try again.");

    public static readonly DomainError RetryNotAllowed =
        new("payments.payment.retry_not_allowed", "Only a failed online payment can be retried.");

    public static readonly DomainError ProviderResultInvalid =
        new("payments.provider_result.invalid", "The provider result is incomplete for its kind.");

    public static readonly DomainError ItemsWithAmount =
        new("payments.items.amount_conflict", "Give either the lines to pay or an amount, not both.");

    public static readonly DomainError ItemNotOnOrder = new("payments.items.not_on_order", "This line is not on the order.");

    public static readonly DomainError ItemAlreadyPaid =
        new("payments.items.already_paid", "Some of these units are already paid by another payment.", DomainErrorKind.Conflict);

    public static readonly DomainError SandboxNotAllowed =
        new("payments.sandbox.not_allowed", "The simulated payment provider is reserved for demonstration restaurants.");

    public static readonly DomainError DemoOnly =
        new("payments.demo.only", "Simulated payments can only be confirmed for a demonstration restaurant.");
}

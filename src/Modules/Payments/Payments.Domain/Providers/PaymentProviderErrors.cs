namespace Payments.Domain;

public static class PaymentProviderErrors
{
    public static readonly DomainError TypeUnknown = new("payments.provider.type_unknown", "Unknown payment provider type.");
    public static readonly DomainError CodeNotUnique = new("payments.provider.code_not_unique", "Another payment provider already uses this code.", DomainErrorKind.Conflict);
    public static readonly DomainError DisplayNameInvalid = new("payments.provider.display_name_invalid", "A payment provider name must be 2 to 60 characters long.");
    public static readonly DomainError AlreadyEnabled = new("payments.provider.already_enabled", "The payment provider is already offered to restaurants.");
    public static readonly DomainError AlreadyDisabled = new("payments.provider.already_disabled", "The payment provider is already withdrawn.");

    public static readonly DomainError MethodsMissing = new("payments.provider.methods_missing", "A payment provider offers at least one payment method.");
    public static readonly DomainError MethodUnknown = new("payments.provider.method_unknown", "Unknown payment method type.");
    public static readonly DomainError MethodNotForType = new("payments.provider.method_not_for_type", "These payment methods or capabilities do not fit an on-site or online provider.");
    public static readonly DomainError CountryInvalid = new("payments.provider.country_invalid", "A country is an ISO 3166-1 alpha-2 code (BJ, FR...).");
    public static readonly DomainError TooManyCountries = new("payments.provider.too_many_countries", "A payment provider cannot list more than 100 countries.");
    public static readonly DomainError TooManyCurrencies = new("payments.provider.too_many_currencies", "A payment provider cannot list more than 50 currencies.");
    public static readonly DomainError PartialRefundWithoutRefund = new("payments.provider.partial_refund_without_refund", "A provider cannot refund partially without refunding at all.");
    public static readonly DomainError OnlineCapabilityOnSite = new("payments.provider.online_capability_on_site", "An on-site provider has no authorization, webhook, recurring payment, platform-managed mode or expiration.");
    public static readonly DomainError ExpirationOutOfRange = new("payments.provider.expiration_out_of_range", "A payment expiration lasts from 1 minute to 30 days.");
    public static readonly DomainError AmountLimitInvalid = new("payments.provider.amount_limit_invalid", "An amount limit has a positive minimum and/or maximum, the minimum not above the maximum.");
    public static readonly DomainError AmountLimitDuplicated = new("payments.provider.amount_limit_duplicated", "A currency has at most one amount limit.");
    public static readonly DomainError AmountLimitCurrencyNotSupported = new("payments.provider.amount_limit_currency_not_supported", "An amount limit must be in a currency the provider supports.");
}

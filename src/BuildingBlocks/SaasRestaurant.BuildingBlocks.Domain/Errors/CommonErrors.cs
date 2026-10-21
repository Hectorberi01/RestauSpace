namespace SaasRestaurant.BuildingBlocks.Domain;

public static class CommonErrors
{
    public static readonly DomainError EmptyId = new("common.id.empty", "An identifier cannot be empty.");
    public static readonly DomainError TenantMismatch = new("common.tenant.mismatch", "The referenced resource belongs to another tenant.");
    public static readonly DomainError EmailEmpty = new("common.email.empty", "Email is required.");
    public static readonly DomainError EmailInvalid = new("common.email.invalid", "Email format is invalid.");
    public static readonly DomainError PhoneInvalid = new("common.phone.invalid", "Phone number must be in international E.164 format.");
    public static readonly DomainError CurrencyUnknown = new("common.currency.unknown", "Currency is not a known ISO 4217 code.");
    public static readonly DomainError CurrencyMismatch = new("common.money.currency_mismatch", "Money operations require the same currency.");
    public static readonly DomainError MoneyOutOfRange = new("common.money.out_of_range", "Amount is beyond the supported range.");
    public static readonly DomainError MoneyPrecision = new("common.money.invalid_precision", "Amount has more decimals than the currency allows.");
    public static readonly DomainError TimeZoneEmpty = new("common.time_zone.empty", "Time zone is required.");
    public static readonly DomainError TimeZoneUnknown = new("common.time_zone.unknown", "Time zone is not a known IANA identifier.");
    public static readonly DomainError PeriodInvalid = new("common.period.invalid", "Period start must be strictly before its end.");
}

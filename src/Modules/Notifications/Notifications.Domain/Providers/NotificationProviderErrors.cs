namespace Notifications.Domain;

public static class NotificationProviderErrors
{
    public static readonly DomainError ChannelUnknown = new("notifications.channel.unknown", "Unknown notification channel.");

    public static readonly DomainError CodeInvalid = new(
        "notifications.provider.code_invalid", "A provider code is 2 to 32 characters: a letter, then letters, digits or '_'.");
    public static readonly DomainError CodeNotUnique = new("notifications.provider.code_not_unique", "Another notification provider already uses this code.", DomainErrorKind.Conflict);
    public static readonly DomainError NameInvalid = new("notifications.provider.name_invalid", "A notification provider name must be 2 to 60 characters long.");
    public static readonly DomainError ConfigurationTypeUnknown = new("notifications.provider.configuration_type_unknown", "Unknown provider configuration type.");
    public static readonly DomainError ConfigurationTypeNotForChannel = new(
        "notifications.provider.configuration_type_not_for_channel",
        "Only in-app notifications are delivered internally, and they are never delivered by an external account.");
    public static readonly DomainError ChannelMismatch = new(
        "notifications.provider.channel_mismatch", "The capabilities describe another channel than the provider's.");
    public static readonly DomainError AlreadyEnabled = new("notifications.provider.already_enabled", "The notification provider is already offered to restaurants.");
    public static readonly DomainError AlreadyDisabled = new("notifications.provider.already_disabled", "The notification provider is already withdrawn.");

    public static readonly DomainError FeatureUnknown = new("notifications.provider.feature_unknown", "Unknown provider feature.");
    public static readonly DomainError ContentFeatureMissing = new(
        "notifications.provider.content_feature_missing", "A provider sends at least text or HTML content.");
    public static readonly DomainError FeatureNotForChannel = new(
        "notifications.provider.feature_not_for_channel", "This feature does not exist on the provider's channel.");
    public static readonly DomainError CountryInvalid = new("notifications.provider.country_invalid", "A country is an ISO 3166-1 alpha-2 code (BJ, FR...).");
    public static readonly DomainError TooManyCountries = new("notifications.provider.too_many_countries", "A provider cannot list more than 100 countries.");
    public static readonly DomainError MaxContentLengthInvalid = new(
        "notifications.provider.max_content_length_invalid", "A maximum content length is between 1 and 1,000,000 characters.");
}

namespace Tenants.Domain.Tenants;

public static class TenantErrors
{
    public static readonly DomainError NameEmpty = new("tenants.tenant_name.empty", "Tenant name is required.");
    public static readonly DomainError NameLengthInvalid = new("tenants.tenant_name.length_invalid", "Tenant name must contain between 2 and 100 characters.");
    public static readonly DomainError NameInvalidCharacters = new("tenants.tenant_name.invalid_characters", "Tenant name cannot contain control characters.");


    public static readonly DomainError BrandingDisplayNameEmpty = new("tenants.branding.display_name_empty", "Branding display name is required.");
    public static readonly DomainError BrandingDisplayNameTooLong = new("tenants.branding.display_name_too_long", "Branding display name cannot exceed 100 characters.");
    public static readonly DomainError BrandingDisplayNameInvalidCharacters = new("tenants.branding.display_name_invalid_characters", "Branding display name cannot contain control characters.");
    public static readonly DomainError BrandingLogoUrlInvalid = new("tenants.branding.logo_url_invalid", "Logo URL must be an absolute http(s) URL.");
    public static readonly DomainError ProfileTaglineTooLong = new("tenants.profile.tagline_too_long", "Tagline cannot exceed 120 characters.");
    public static readonly DomainError ProfileDescriptionTooLong = new("tenants.profile.description_too_long", "Description cannot exceed 2000 characters.");
    public static readonly DomainError ProfileTextInvalidCharacters = new("tenants.profile.invalid_characters", "Tagline and description cannot contain control characters.");
    public static readonly DomainError ProfileImageUrlInvalid = new("tenants.profile.image_url_invalid", "Cover image URL must be an absolute http(s) URL.");
    public static readonly DomainError ProfileLinkInvalid = new("tenants.profile.link_invalid", "Each link needs a known network and an absolute http(s) URL.");
    public static readonly DomainError ProfileLinkDuplicated = new("tenants.profile.link_duplicated", "Only one link per network is allowed.");
    public static readonly DomainError ProfileTooManyLinks = new("tenants.profile.too_many_links", "Too many links.");
    public static readonly DomainError BrandingColorInvalid = new("tenants.branding.color_invalid", "Primary color must be a hexadecimal color such as #1A2B3C or #ABC.");

    public static readonly DomainError Closed = new("tenants.tenant.closed", "The tenant is closed; no further change is allowed.");
    public static readonly DomainError Suspended = new("tenants.tenant.suspended", "The tenant is suspended; business writes are not allowed.");
    public static readonly DomainError OnlyTrialCanBeActivated = new("tenants.tenant.only_trial_can_be_activated", "Only a tenant in trial can be activated.");
    public static readonly DomainError AlreadySuspended = new("tenants.tenant.already_suspended", "The tenant is already suspended.");
    public static readonly DomainError NotSuspended = new("tenants.tenant.not_suspended", "Only a suspended tenant can be reactivated.");
    public static readonly DomainError CurrencyLockedByPricedCatalog = new("tenants.tenant.currency_locked_by_priced_catalog", "The default currency cannot change once the catalogue has priced products.");
    public static readonly DomainError SuspensionReasonInvalid = new("tenants.tenant.suspension_reason_invalid", "Suspension reason is not a known value.");
}

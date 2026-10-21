namespace Payments.Domain;

public static class PaymentConfigurationErrors
{
    public static readonly DomainError ChannelUnknown = new("payments.configuration.channel_unknown", "The payment channel is not recognized.");
    public static readonly DomainError ProviderUnavailable = new("payments.configuration.provider_unavailable", "The platform does not offer this payment provider at the moment.");
    public static readonly DomainError ProviderMismatch = new("payments.configuration.provider_mismatch", "This provider is not the one of the configuration.");
    public static readonly DomainError AlreadyExists = new("payments.configuration.already_exists", "The restaurant already has a configuration for this payment provider.", DomainErrorKind.Conflict);
    public static readonly DomainError ModeUnknown = new("payments.configuration.mode_unknown", "Unknown payment mode.");
    public static readonly DomainError ModeNotApplicable = new("payments.configuration.mode_not_applicable", "An on-site payment provider is always managed by the restaurant.");
    public static readonly DomainError PlatformModeNotAllowed = new("payments.configuration.platform_mode_not_allowed", "This provider does not allow the platform to collect payments on behalf of restaurants.");
    public static readonly DomainError CredentialsNotApplicable = new("payments.configuration.credentials_not_applicable", "Only an online provider managed by the restaurant has merchant credentials.");
    public static readonly DomainError CredentialsMissing = new("payments.configuration.credentials_missing", "Enter the merchant credentials before enabling this online payment provider.");
    public static readonly DomainError CredentialReferenceInvalid = new("payments.configuration.credential_reference_invalid", "Unknown credential reference format.");
    public static readonly DomainError AlreadyEnabled = new("payments.configuration.already_enabled", "The payment configuration is already enabled.");
    public static readonly DomainError AlreadyDisabled = new("payments.configuration.already_disabled", "The payment configuration is already disabled.");
    public static readonly DomainError MethodNotOffered = new("payments.configuration.method_not_offered", "The payment provider does not offer this payment method.");
    public static readonly DomainError MethodNotActive = new("payments.configuration.method_not_active", "Activate this payment method in the configuration before assigning it to a location.");
    public static readonly DomainError LastMethod = new("payments.configuration.last_method", "A configuration keeps at least one payment method; disable the configuration instead.");
    public static readonly DomainError PriorityOutOfRange = new("payments.configuration.priority_out_of_range", "A priority is between 0 and 1000.");
    public static readonly DomainError AmountBoundsInvalid = new("payments.configuration.amount_bounds_invalid", "Amount bounds are positive, in one currency, the minimum not above the maximum.");
    public static readonly DomainError TooManyAssignments = new("payments.configuration.too_many_assignments", "A configuration cannot have more than 500 location assignments.");
    public static readonly DomainError AssignmentNotFound = new("payments.configuration.assignment_not_found", "This location has no such assignment in the configuration.");
    public static readonly DomainError Disabled = new("payments.configuration.disabled", "This payment configuration is disabled: it takes no new payment.");
    public static readonly DomainError MethodNotAccepted = new("payments.configuration.method_not_accepted", "The restaurant does not accept this payment method with this provider.");
    public static readonly DomainError NotAssignedToLocation = new("payments.configuration.not_assigned_to_location", "This payment method is not offered at this location.");
    public static readonly DomainError AmountOutOfBounds = new("payments.configuration.amount_out_of_bounds", "The amount is outside the bounds set for this payment method at this location.");
}

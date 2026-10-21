namespace Notifications.Domain;

public static class NotificationConfigurationErrors
{
    public static readonly DomainError ProviderUnavailable = new(
        "notifications.configuration.provider_unavailable", "The platform does not offer this notification provider at the moment.");
    public static readonly DomainError ProviderMismatch = new(
        "notifications.configuration.provider_mismatch", "This provider is not the one of the configuration.");
    public static readonly DomainError ChannelMismatch = new(
        "notifications.configuration.channel_mismatch", "A configuration only changes to a provider of the same channel.");
    public static readonly DomainError AlreadyExists = new(
        "notifications.configuration.already_exists", "The restaurant already has a configuration for this notification provider.", DomainErrorKind.Conflict);
    public static readonly DomainError ModeUnknown = new("notifications.configuration.mode_unknown", "Unknown provider mode.");
    public static readonly DomainError ModeNotAllowed = new(
        "notifications.configuration.mode_not_allowed", "The platform does not allow this provider to be used with this account (platform or restaurant).");
    public static readonly DomainError CredentialsNotApplicable = new(
        "notifications.configuration.credentials_not_applicable", "Only a configuration using the restaurant's own account has credentials.");
    public static readonly DomainError CredentialsForAnotherProvider = new(
        "notifications.configuration.credentials_for_another_provider", "These credentials belong to another provider.");
    public static readonly DomainError CredentialsMissing = new(
        "notifications.configuration.credentials_missing", "Enter the credentials of the restaurant's account before enabling this provider.");
    public static readonly DomainError CredentialReferenceInvalid = new(
        "notifications.configuration.credential_reference_invalid", "Unknown credential reference format.");
    public static readonly DomainError AlreadyEnabled = new("notifications.configuration.already_enabled", "The notification configuration is already enabled.");
    public static readonly DomainError AlreadyDisabled = new("notifications.configuration.already_disabled", "The notification configuration is already disabled.");
    public static readonly DomainError PriorityOutOfRange = new("notifications.configuration.priority_out_of_range", "A priority is between 0 and 1000.");
    public static readonly DomainError TooManyAssignments = new(
        "notifications.configuration.too_many_assignments", "A configuration cannot have more than 500 location assignments.");
    public static readonly DomainError AssignmentNotFound = new(
        "notifications.configuration.assignment_not_found", "This location is not assigned to the configuration.");
    public static readonly DomainError Disabled = new("notifications.configuration.disabled", "This notification configuration is disabled: it sends nothing.");
    public static readonly DomainError NotAssignedToLocation = new(
        "notifications.configuration.not_assigned_to_location", "This channel is not offered at this location with this provider.");
}

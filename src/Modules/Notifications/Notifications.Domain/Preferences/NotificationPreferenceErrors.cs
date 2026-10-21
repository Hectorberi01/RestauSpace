namespace Notifications.Domain;

public static class NotificationPreferenceErrors
{
    public static readonly DomainError AlreadyExists = new("notifications.preference.already_exists", "This person already has notification preferences in this restaurant.", DomainErrorKind.Conflict);
    public static readonly DomainError PurposeUnknown = new("notifications.preference.purpose_unknown", "Unknown notification purpose.");
    public static readonly DomainError SourceInvalid = new("notifications.preference.source_invalid", "The source must be a short code made of lowercase letters and digits separated by '.', '_' or '-'.");
    public static readonly DomainError TypesForStaffOnly = new("notifications.preference.types_for_staff_only", "Only a team member chooses alerts type by type.");
    public static readonly DomainError TypeNotMutable = new("notifications.preference.type_not_mutable", "Only team alerts can be turned off one by one.");
    public static readonly DomainError MarketingForCustomersOnly = new("notifications.preference.marketing_for_customers_only", "Only a customer receives marketing messages.");
}

namespace Customers.Domain;

public static class ConsentErrors
{
    public static readonly DomainError SourceRequired = new("customers.consent.source_required", "The source of a consent change is required.");
    public static readonly DomainError SourceTooLong = new("customers.consent.source_too_long", "The source of a consent change is too long.");
    public static readonly DomainError SourceInvalid = new("customers.consent.source_invalid", "The source must be a short code made of lowercase letters and digits separated by '.', '_' or '-'.");
    public static readonly DomainError ReasonInvalid = new("customers.consent.reason_invalid", "The withdrawal reason is not supported.");
    public static readonly DomainError ChannelNotSupported = new("customers.consent.channel_not_supported", "This communication channel is not supported.");
    public static readonly DomainError NotUnique = new("customers.consent.not_unique", "A consent already exists for this customer and channel.", DomainErrorKind.Conflict);
    public static readonly DomainError CustomerAnonymized = new("customers.consent.customer_anonymized", "No consent can be granted for an anonymized customer.");
    public static readonly DomainError CustomerMismatch = new("customers.consent.customer_mismatch", "This consent belongs to another customer.");
    public static readonly DomainError AlreadyGranted = new("customers.consent.already_granted", "The consent is already granted.");
    public static readonly DomainError AlreadyWithdrawn = new("customers.consent.already_withdrawn", "The consent is already withdrawn.");
}

namespace Customers.Domain;

public static class CustomerErrors
{
    public static readonly DomainError NameRequired = new("customers.customer.name_required", "Customer name is required.");
    public static readonly DomainError NameTooLong = new("customers.customer.name_too_long", "Customer name is too long.");
    public static readonly DomainError ContactRequired = new("customers.customer.contact_required", "The contact policy requires a contact identifier that is missing.");
    public static readonly DomainError Anonymized = new("customers.customer.anonymized", "The customer has been anonymized and can no longer be changed.");
    public static readonly DomainError Merged = new("customers.customer.merged", "This customer record was merged into another one.");
    public static readonly DomainError MergeWithItself = new("customers.customer.merge_with_itself", "Choose two different customer records to merge.");
    public static readonly DomainError TagAlreadyPresent = new("customers.customer.tag_already_present", "The customer already has this tag.");
    public static readonly DomainError TagNotFound = new("customers.customer.tag_not_found", "The customer does not have this tag.");
    public static readonly DomainError TooManyTags = new("customers.customer.too_many_tags", "The customer has reached the maximum number of tags.");

    public static readonly DomainError ContactRequirementUnknown = new("customers.contact_policy.requirement_unknown", "This contact requirement is not supported.");

    public static readonly DomainError TagInvalid = new("customers.tag.invalid", "A tag must be 1 to 30 letters, digits, spaces, dashes or underscores.");

    public static readonly DomainError LanguageInvalid = new("customers.preference.language_invalid", "Language must be a BCP-47 style code such as 'fr' or 'fr-FR'.");
    public static readonly DomainError LanguageNotSupported = new("customers.preference.language_not_supported", "This language is not supported.");
    public static readonly DomainError ChannelNotSupported = new("customers.preference.channel_not_supported", "This communication channel is not supported.");
}

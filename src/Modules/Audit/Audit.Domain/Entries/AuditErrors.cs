namespace Audit.Domain;

public static class AuditErrors
{
    public static readonly DomainError ActorTypeInvalid = new("audit.actor.type_invalid", "Actor type is not supported.");
    public static readonly DomainError ActorValueInvalid = new("audit.actor.value_invalid", "Actor identifier is required, limited to 100 characters, without control characters, and a GUID for a user.");
    public static readonly DomainError ActionInvalid = new("audit.action.invalid", "Audit action must be a lowercase dotted code such as 'orders.order.cancelled'.");
    public static readonly DomainError TargetTypeInvalid = new("audit.target.type_invalid", "Target type must be a code of letters, digits, '_' or '.' (100 characters max).");
    public static readonly DomainError TargetIdInvalid = new("audit.target.id_invalid", "Target identifier is required, limited to 200 characters, without control characters.");
    public static readonly DomainError CorrelationIdInvalid = new("audit.correlation_id.invalid", "Correlation id is required, without whitespace, and limited to 100 characters.");
    public static readonly DomainError MetadataKeyInvalid = new("audit.metadata.key_invalid", "Metadata keys are required and limited to 100 characters.");
    public static readonly DomainError MetadataKeyDuplicated = new("audit.metadata.key_duplicated", "Metadata keys must be unique (case-insensitive).");
    public static readonly DomainError MetadataValueMissing = new("audit.metadata.value_missing", "Metadata values cannot be null.");
    public static readonly DomainError MetadataValueTooLong = new("audit.metadata.value_too_long", "Metadata values are limited to 1000 characters.");
    public static readonly DomainError MetadataTooManyEntries = new("audit.metadata.too_many_entries", "Metadata is limited to 50 entries.");
}

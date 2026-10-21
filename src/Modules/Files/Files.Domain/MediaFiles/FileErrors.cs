namespace Files.Domain;

public static class FileErrors
{
    public static readonly DomainError FileNameEmpty = new("files.file_name.empty", "File name is required.");
    public static readonly DomainError FileNameTooLong = new("files.file_name.too_long", "File name is limited to 255 characters.");
    public static readonly DomainError FileNameInvalid = new("files.file_name.invalid", "File name cannot contain path separators, control, invisible or direction-changing characters, be made only of dots, nor be a reserved device name.");
    public static readonly DomainError ContentTypeInvalid = new("files.content_type.invalid", "Content type must be a MIME type of the form 'type/subtype'.");
    public static readonly DomainError StorageKeyInvalid = new("files.storage_key.invalid", "Storage key is required, without whitespace, and limited to 1024 characters.");
    public static readonly DomainError OwnerTypeInvalid = new("files.owner.type_invalid", "Owner type must be a lowercase dotted code of at least two segments such as 'catalog.product'.");
    public static readonly DomainError OwnerIdEmpty = new("files.owner.id_empty", "Owner identifier cannot be empty.");
    public static readonly DomainError PolicyMaxSizeInvalid = new("files.upload_policy.max_size_invalid", "Maximum file size must be greater than zero.");
    public static readonly DomainError PolicyContentTypesEmpty = new("files.upload_policy.content_types_empty", "An upload policy must allow at least one content type.");

    public static readonly DomainError SizeNotPositive = new("files.media_file.size_not_positive", "File size must be greater than zero.");
    public static readonly DomainError SizeExceedsLimit = new("files.media_file.size_exceeds_limit", "File size exceeds the allowed limit.");
    public static readonly DomainError ContentTypeNotAllowed = new("files.media_file.content_type_not_allowed", "This content type is not allowed.");
    public static readonly DomainError ActualSizeMismatch = new("files.media_file.actual_size_mismatch", "The stored size differs from the declared size.");
    public static readonly DomainError ContentTypeMismatch = new("files.media_file.content_type_mismatch", "The detected content type differs from the declared content type.");
    public static readonly DomainError NotPending = new("files.media_file.not_pending", "Only a pending file can become available.");
    public static readonly DomainError AlreadyQuarantined = new("files.media_file.already_quarantined", "The file is already quarantined.");
    public static readonly DomainError QuarantineReasonInvalid = new("files.media_file.quarantine_reason_invalid", "A quarantine reason is required and limited to 500 characters.");
    public static readonly DomainError ThumbnailOfThumbnail = new("files.media_file.thumbnail_of_thumbnail", "A thumbnail has no thumbnail of its own.");
    public static readonly DomainError ThumbnailOriginalUnavailable = new("files.media_file.thumbnail_original_unavailable", "The photo of this thumbnail is not available.");
    public static readonly DomainError Deleted = new("files.media_file.deleted", "A deleted file cannot be changed.");
}

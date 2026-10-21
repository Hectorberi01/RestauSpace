namespace Files.Domain;

/// <summary>Règles d'upload appliquées à la déclaration d'un fichier : taille maximale et types MIME autorisés.</summary>
public sealed class FileUploadPolicy : ValueObject
{
    public const long DefaultMaxSizeInBytes = 10L * 1024 * 1024;

    private readonly HashSet<ContentType> _allowedContentTypes;

    private FileUploadPolicy(long maxSizeInBytes, HashSet<ContentType> allowedContentTypes)
    {
        MaxSizeInBytes = maxSizeInBytes;
        _allowedContentTypes = allowedContentTypes;
    }

    /// <summary>10 Mo ; images JPEG, PNG, WebP et documents PDF.</summary>
    public static FileUploadPolicy Default { get; } = Create(
        DefaultMaxSizeInBytes,
        [
            ContentType.Create("image/jpeg"),
            ContentType.Create("image/png"),
            ContentType.Create("image/webp"),
            ContentType.Create("application/pdf"),
        ]);

    public long MaxSizeInBytes { get; }

    public IReadOnlyCollection<ContentType> AllowedContentTypes => _allowedContentTypes.ToList().AsReadOnly();

    public static FileUploadPolicy Create(long maxSizeInBytes, IEnumerable<ContentType> allowedContentTypes)
    {
        ArgumentNullException.ThrowIfNull(allowedContentTypes);
        DomainException.ThrowIf(maxSizeInBytes <= 0, FileErrors.PolicyMaxSizeInvalid);

        var allowed = new HashSet<ContentType>();
        foreach (var contentType in allowedContentTypes)
        {
            ArgumentNullException.ThrowIfNull(contentType, nameof(allowedContentTypes));
            allowed.Add(contentType);
        }

        DomainException.ThrowIf(allowed.Count == 0, FileErrors.PolicyContentTypesEmpty);

        return new FileUploadPolicy(maxSizeInBytes, allowed);
    }

    public bool Allows(ContentType contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        return _allowedContentTypes.Contains(contentType);
    }

    public bool AllowsSize(long sizeInBytes) => sizeInBytes > 0 && sizeInBytes <= MaxSizeInBytes;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxSizeInBytes;
        foreach (var contentType in _allowedContentTypes.OrderBy(c => c.Value, StringComparer.Ordinal))
        {
            yield return contentType;
        }
    }
}

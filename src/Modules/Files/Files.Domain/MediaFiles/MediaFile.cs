namespace Files.Domain;

/// <summary>Métadonnées et cycle de vie d'un objet stocké. Le binaire lui-même n'entre jamais dans le domaine.</summary>
public sealed class MediaFile : AggregateRoot<FileId>, ITenantOwned
{
    public const int MaxQuarantineReasonLength = 500;

    /// <summary>Une miniature est préparée par le navigateur (petite largeur, compressée) : 512 Ko au plus.</summary>
    public const long MaxThumbnailSizeInBytes = 512 * 1024;

    private MediaFile(
        FileId id,
        TenantId tenantId,
        FileOwner owner,
        FileName fileName,
        ContentType contentType,
        long sizeInBytes,
        long maxSizeInBytes,
        StorageKey storageKey,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Owner = owner;
        FileName = fileName;
        ContentType = contentType;
        SizeInBytes = sizeInBytes;
        MaxSizeInBytes = maxSizeInBytes;
        StorageKey = storageKey;
        Status = MediaFileStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private MediaFile()
    {
        Owner = null!;
        FileName = null!;
        ContentType = null!;
        StorageKey = null!;
    }

    public TenantId TenantId { get; private set; }

    public FileOwner Owner { get; private set; }

    public FileName FileName { get; private set; }

    public ContentType ContentType { get; private set; }

    /// <summary>Déclarée par l'émetteur ; prouvée égale à la taille stockée par <see cref="MarkAvailable"/>.</summary>
    public long SizeInBytes { get; private set; }

    /// <summary>
    /// Taille maximale de la politique d'upload, figée à la création : le contrôle de la taille stockée ne dépend pas
    /// d'une politique passée (ou assouplie) appel par appel.
    /// </summary>
    public long MaxSizeInBytes { get; private set; }

    public StorageKey StorageKey { get; private set; }

    public MediaFileStatus Status { get; private set; }

    public string? QuarantineReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? AvailableAtUtc { get; private set; }

    public DateTimeOffset? QuarantinedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>La photo dont ce fichier est la miniature (même propriétaire) ; null pour un fichier ordinaire.</summary>
    public FileId? ThumbnailOf { get; private set; }

    public bool IsThumbnail => ThumbnailOf is not null;

    /// <summary>Seul un fichier disponible peut être servi.</summary>
    public bool CanBeServed => Status == MediaFileStatus.Available;

    public static MediaFile CreatePending(
        TenantId tenantId,
        FileOwner owner,
        FileName fileName,
        ContentType contentType,
        long sizeInBytes,
        StorageKey storageKey,
        FileUploadPolicy policy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(storageKey);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);

        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(sizeInBytes <= 0, FileErrors.SizeNotPositive);
        DomainException.ThrowIf(sizeInBytes > policy.MaxSizeInBytes, FileErrors.SizeExceedsLimit);
        DomainException.ThrowIf(!policy.Allows(contentType), FileErrors.ContentTypeNotAllowed);

        return new MediaFile(
            FileId.New(), tenantId, owner, fileName, contentType, sizeInBytes, policy.MaxSizeInBytes, storageKey, clock.UtcNow);
    }

    /// <summary>
    /// La miniature d'une photo (même restaurant, même propriétaire), plus légère pour les listes et les écrans mobiles.
    /// Même cycle que tout fichier : envoyée, vérifiée, puis disponible ; supprimée avec sa photo.
    /// </summary>
    public static MediaFile CreateThumbnailPending(
        MediaFile original,
        FileName fileName,
        ContentType contentType,
        long sizeInBytes,
        StorageKey storageKey,
        FileUploadPolicy policy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(policy);
        DomainException.ThrowIf(original.IsThumbnail, FileErrors.ThumbnailOfThumbnail);
        DomainException.ThrowIf(original.Status is MediaFileStatus.Deleted or MediaFileStatus.Quarantined, FileErrors.ThumbnailOriginalUnavailable);
        DomainException.ThrowIf(sizeInBytes > MaxThumbnailSizeInBytes, FileErrors.SizeExceedsLimit);

        var thumbnail = CreatePending(
            original.TenantId,
            original.Owner,
            fileName,
            contentType,
            sizeInBytes,
            storageKey,
            FileUploadPolicy.Create(Math.Min(policy.MaxSizeInBytes, MaxThumbnailSizeInBytes), policy.AllowedContentTypes),
            clock);
        thumbnail.ThumbnailOf = original.Id;
        return thumbnail;
    }

    /// <summary>
    /// Le binaire est stocké : confronte ce que l'émetteur a déclaré à ce que le stockage et l'antivirus ont observé.
    /// Toute différence est rejetée et laisse le fichier Pending ; l'appelant appelle alors <see cref="Quarantine"/>.
    /// </summary>
    /// <param name="actualSizeInBytes">Taille de l'objet stocké, mesurée par le stockage.</param>
    /// <param name="detectedContentType">Type déduit du contenu (magic bytes), jamais de l'en-tête de l'émetteur.</param>
    public void MarkAvailable(long actualSizeInBytes, ContentType detectedContentType, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(detectedContentType);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDeleted();
        DomainException.ThrowIf(Status != MediaFileStatus.Pending, FileErrors.NotPending);
        DomainException.ThrowIf(actualSizeInBytes <= 0, FileErrors.SizeNotPositive);
        DomainException.ThrowIf(actualSizeInBytes > MaxSizeInBytes, FileErrors.SizeExceedsLimit);
        DomainException.ThrowIf(actualSizeInBytes != SizeInBytes, FileErrors.ActualSizeMismatch);
        DomainException.ThrowIf(detectedContentType != ContentType, FileErrors.ContentTypeMismatch);

        var now = clock.UtcNow;
        Status = MediaFileStatus.Available;
        AvailableAtUtc = now;

        Raise(new FileUploaded(TenantId, Id, Owner.OwnerType, Owner.OwnerId, ContentType.Value, SizeInBytes, now));
    }

    public void Quarantine(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDeleted();
        DomainException.ThrowIf(Status == MediaFileStatus.Quarantined, FileErrors.AlreadyQuarantined);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(reason), FileErrors.QuarantineReasonInvalid);

        var trimmedReason = reason!.Trim();
        DomainException.ThrowIf(trimmedReason.Length > MaxQuarantineReasonLength, FileErrors.QuarantineReasonInvalid);

        var now = clock.UtcNow;
        Status = MediaFileStatus.Quarantined;
        QuarantineReason = trimmedReason;
        QuarantinedAtUtc = now;

        Raise(new FileQuarantined(TenantId, Id, Owner.OwnerType, Owner.OwnerId, trimmedReason, now));
    }

    public void Delete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDeleted();

        var now = clock.UtcNow;
        Status = MediaFileStatus.Deleted;
        DeletedAtUtc = now;

        Raise(new FileDeleted(TenantId, Id, Owner.OwnerType, Owner.OwnerId, StorageKey.Value, now));
    }

    private void EnsureNotDeleted() => DomainException.ThrowIf(Status == MediaFileStatus.Deleted, FileErrors.Deleted);
}

using Files.Domain;

namespace Files.Application.Abstractions;

// Ports du module Files. Le tenant est TOUJOURS donné explicitement : celui du contexte côté restaurant ; seules les
// lectures du job (IAbandonedUploads) voient tous les tenants.

public interface IMediaFileRepository
{
    Task<MediaFile?> FindAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les fichiers non supprimés d'un propriétaire, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<MediaFile>> ListByOwnerAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les fichiers disponibles de plusieurs propriétaires d'un même type, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<MediaFile>> ListAvailableByOwnersAsync(
        TenantId tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lien public (requête anonyme, sans tenant de contexte) : le fichier du tenant donné, lu explicitement dans ce
    /// tenant. Lecture seule.
    /// </summary>
    Task<MediaFile?> FindForPublicLinkAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken = default);

    void Add(MediaFile file);
}

/// <summary>Un upload abandonné à nettoyer (job de la plateforme) : (tenant, fichier).</summary>
public sealed record AbandonedUpload(TenantId TenantId, FileId FileId);

/// <summary>Pour le job SEULEMENT : tous les tenants.</summary>
public interface IAbandonedUploads
{
    Task<IReadOnlyList<AbandonedUpload>> ListAsync(DateTimeOffset createdBeforeUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>Ce que le stockage sait d'un objet : sa taille réelle et ses premiers octets (pour reconnaître le type).</summary>
public sealed record StoredObject(long SizeInBytes, byte[] Head);

/// <summary>
/// Le stockage objet (Cloudflare R2, compatible S3). Les URLs signées donnent un accès direct et limité dans le temps à
/// UN objet ; elles ne sont jamais journalisées. Les identifiants du compte de stockage ne sortent jamais de
/// l'implémentation.
/// </summary>
public interface IObjectStorage
{
    /// <summary>URL d'envoi direct (PUT) : type et taille exacts signés, valable <paramref name="lifetime"/>.</summary>
    Uri PresignUpload(StorageKey key, ContentType contentType, long sizeInBytes, TimeSpan lifetime);

    /// <summary>URL de lecture (GET) téléchargée sous <paramref name="fileName"/>, valable <paramref name="lifetime"/>.</summary>
    Uri PresignDownload(StorageKey key, FileName fileName, TimeSpan lifetime);

    /// <summary>Taille et premiers octets de l'objet ; null s'il n'existe pas.</summary>
    Task<StoredObject?> InspectAsync(StorageKey key, int headLength, CancellationToken cancellationToken = default);

    Task PutAsync(StorageKey key, ContentType contentType, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Le contenu entier ; null s'il n'existe pas.</summary>
    Task<byte[]?> GetAsync(StorageKey key, CancellationToken cancellationToken = default);

    /// <summary>Idempotent : supprimer un objet absent n'est pas une erreur.</summary>
    Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default);
}

/// <summary>Résultat d'une analyse antivirus : null = propre, sinon la raison (sans contenu du fichier).</summary>
public interface IFileScanner
{
    Task<string?> ScanAsync(StorageKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le propriétaire existe-t-il dans CE tenant ? Fourni par l'hôte (Catalog, Locations, Tenants) : Files ne connaît aucun
/// autre module. Inconnu ou autre tenant = non.
/// </summary>
public interface IFileOwnerChecker
{
    Task<bool> BelongsToTenantAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Files doit savoir du tenant, fourni par l'hôte depuis Tenants.</summary>
public interface IFileTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

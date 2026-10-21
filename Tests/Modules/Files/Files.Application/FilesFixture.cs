using Files.Application.Storage;
using Files.Application.Abstractions;
using Files.Application.Commands;
using Files.Application.Queries;
using Files.Domain;

namespace Files.Application;

/// <summary>
/// Ports en mémoire du module Files, stockage compris. Comme les vrais, ils ne répondent que pour le tenant donné. Le
/// tenant de « la requête » est <see cref="CurrentTenantId"/> ; les propriétaires connus sont dans <see cref="Owners"/>.
/// </summary>
public sealed class FilesFixture : ITenantContext, IFileTenants, IMediaFileRepository, IObjectStorage, IFileScanner, IFileOwnerChecker, IFilesUnitOfWork
{
    public FilesFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    /// <summary>Propriétaires existants : (tenant, code de type, id).</summary>
    public HashSet<(TenantId, string, Guid)> Owners { get; } = [];

    public List<MediaFile> Files { get; } = [];

    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    /// <summary>Réponse de l'antivirus : null = propre.</summary>
    public string? Threat { get; set; }

    public FilesSettings Settings { get; } = new();

    public FakeClock Clock { get; } = new();

    public FilesWorkspace Workspace => new(this, this, this, this, this, this, Settings, this);

    public RequestUploadHandler Request => new(Workspace, Clock);

    public CompleteUploadHandler Complete => new(Workspace, Clock);

    public StoreGeneratedFileHandler StoreGenerated => new(Workspace, Clock);

    public DeleteFileHandler Delete => new(Workspace, Clock);

    public DeleteOwnedFileHandler DeleteOwned => new(Workspace, Clock);

    public DeleteAbandonedUploadHandler DeleteAbandoned => new(Workspace, Clock);

    public GetFileHandler Get => new(Workspace);

    public ListOwnerFilesHandler List => new(Workspace);

    public GetDownloadLinkHandler Download => new(Workspace, Clock);

    public ReadFileContentHandler Read => new(Workspace);

    public PublicMediaLinks Links { get; set; } = new(new Uri("https://api.test"), Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public ListPublicImagesHandler PublicImages => new(Workspace, Links);

    public ReadPublicMediaHandler PublicMedia => new(this, this, this, Links);

    /// <summary>Un produit du restaurant courant.</summary>
    public Guid Product(TenantId? tenant = null)
    {
        var id = Guid.NewGuid();
        Owners.Add((tenant ?? CurrentTenantId!.Value, "catalog.product", id));
        return id;
    }

    public MediaFile FileById(Guid id) => Files.Single(file => file.Id.Value == id);

    /// <summary>Le navigateur envoie les octets vers l'URL signée du fichier.</summary>
    public void UploadBytes(Guid fileId, byte[] content) => Objects[FileById(fileId).StorageKey.Value] = content;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<bool> IFileTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<MediaFile?> IMediaFileRepository.FindAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken) =>
        Task.FromResult<MediaFile?>(Files.SingleOrDefault(file => file.TenantId == tenantId && file.Id == id));

    Task<IReadOnlyList<MediaFile>> IMediaFileRepository.ListByOwnerAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaFile>>(Files
            .Where(file => file.TenantId == tenantId && file.Owner == owner && file.Status != MediaFileStatus.Deleted)
            .OrderByDescending(file => file.CreatedAtUtc)
            .ToList());

    Task<IReadOnlyList<MediaFile>> IMediaFileRepository.ListAvailableByOwnersAsync(
        TenantId tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaFile>>(Files
            .Where(file => file.TenantId == tenantId && file.Status == MediaFileStatus.Available
                && file.Owner.OwnerType == ownerType && ownerIds.Contains(file.Owner.OwnerId))
            .OrderBy(file => file.CreatedAtUtc)
            .ToList());

    Task<MediaFile?> IMediaFileRepository.FindForPublicLinkAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken) =>
        Task.FromResult(Files.SingleOrDefault(file => file.TenantId == tenantId && file.Id == id));

    void IMediaFileRepository.Add(MediaFile file) => Files.Add(file);

    Uri IObjectStorage.PresignUpload(StorageKey key, ContentType contentType, long sizeInBytes, TimeSpan lifetime) =>
        new($"https://storage.test/upload/{key.Value}");

    Uri IObjectStorage.PresignDownload(StorageKey key, FileName fileName, TimeSpan lifetime) => new($"https://storage.test/download/{key.Value}");

    Task<StoredObject?> IObjectStorage.InspectAsync(StorageKey key, int headLength, CancellationToken cancellationToken) =>
        Task.FromResult<StoredObject?>(Objects.TryGetValue(key.Value, out var content)
            ? new StoredObject(content.LongLength, content[..Math.Min(headLength, content.Length)])
            : null);

    Task IObjectStorage.PutAsync(StorageKey key, ContentType contentType, byte[] content, CancellationToken cancellationToken)
    {
        Objects[key.Value] = content;
        return Task.CompletedTask;
    }

    Task<byte[]?> IObjectStorage.GetAsync(StorageKey key, CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(Objects.GetValueOrDefault(key.Value));

    Task IObjectStorage.DeleteAsync(StorageKey key, CancellationToken cancellationToken)
    {
        Objects.Remove(key.Value);
        return Task.CompletedTask;
    }

    Task<string?> IFileScanner.ScanAsync(StorageKey key, CancellationToken cancellationToken) => Task.FromResult<string?>(Threat);

    Task<bool> IFileOwnerChecker.BelongsToTenantAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken) =>
        Task.FromResult(Owners.Contains((tenantId, owner.OwnerType, owner.OwnerId)));
}

/// <summary>Contenus de test reconnaissables à leurs premiers octets.</summary>
public static class Samples
{
    public static byte[] Png(int size = 64)
    {
        var content = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(content, 0);
        return content;
    }

    /// <summary>Un exécutable Windows (« MZ ») présenté comme une image.</summary>
    public static byte[] Executable(int size = 64)
    {
        var content = new byte[size];
        content[0] = (byte)'M';
        content[1] = (byte)'Z';
        return content;
    }
}

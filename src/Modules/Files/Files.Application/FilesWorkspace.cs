using Files.Application.Abstractions;
using Files.Domain;

namespace Files.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Le tenant vient du contexte (requête ou job), jamais d'une valeur de requête ; une
/// écriture du restaurant exige un tenant qui les accepte. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class FilesWorkspace(
    ITenantContext tenantContext,
    IFileTenants tenants,
    IMediaFileRepository files,
    IObjectStorage storage,
    IFileScanner scanner,
    IFileOwnerChecker owners,
    FilesSettings settings,
    IFilesUnitOfWork unitOfWork)
{
    public IMediaFileRepository Files => files;

    public IObjectStorage Storage => storage;

    public IFileScanner Scanner => scanner;

    public IFileOwnerChecker Owners => owners;

    public FilesSettings Settings => settings;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(FilesApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<MediaFile> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await files.FindAsync(CurrentTenant, FileId.From(id), cancellationToken))
        ?? throw new NotFoundException(FilesApplicationErrors.FileNotFound);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

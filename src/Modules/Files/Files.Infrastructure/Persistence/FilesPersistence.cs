using Files.Application;
using Files.Application.Abstractions;
using Files.Domain;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Files.Infrastructure.Persistence;

internal sealed class MediaFileRepository(FilesDbContext context) : IMediaFileRepository
{
    public Task<MediaFile?> FindAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken = default) =>
        context.Files.SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MediaFile>> ListByOwnerAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken = default) =>
        await context.Files.AsNoTracking()
            .Where(file => file.TenantId == tenantId && file.Owner == owner && file.Status != MediaFileStatus.Deleted)
            .OrderByDescending(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MediaFile>> ListAvailableByOwnersAsync(
        TenantId tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default)
    {
        if (ownerIds.Count == 0)
        {
            return [];
        }

        var owners = ownerIds.Distinct().Select(id => FileOwner.Create(ownerType, id)).ToList();
        return await context.Files.AsNoTracking()
            .Where(file => file.TenantId == tenantId && file.Status == MediaFileStatus.Available && owners.Contains(file.Owner))
            .OrderBy(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    // Sans tenant de contexte : le filtre global est levé et le tenant du lien signé est imposé explicitement.
    public Task<MediaFile?> FindForPublicLinkAsync(TenantId tenantId, FileId id, CancellationToken cancellationToken = default) =>
        context.Files.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == id, cancellationToken);

    public void Add(MediaFile file) => context.Files.Add(file);
}

/// <summary>Le job : tous tenants, déclaré (IgnoreQueryFilters). Ne renvoie que des (tenant, id).</summary>
internal sealed class AbandonedUploads(FilesDbContext context) : IAbandonedUploads
{
    public async Task<IReadOnlyList<AbandonedUpload>> ListAsync(DateTimeOffset createdBeforeUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Files.IgnoreQueryFilters().AsNoTracking()
            .Where(file => file.Status == MediaFileStatus.Pending && file.CreatedAtUtc <= createdBeforeUtc)
            .OrderBy(file => file.CreatedAtUtc)
            .Select(file => new { file.TenantId, file.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new AbandonedUpload(row.TenantId, row.Id))
        .ToList();
}

internal sealed class FilesUnitOfWork(FilesDbContext context) : OutboxUnitOfWork<FilesDbContext>(context), IFilesUnitOfWork
{
    protected override DomainError ConcurrentUpdate => FilesApplicationErrors.ConcurrentUpdate;

    /// <summary>Clé de stockage en double (garde finale) : une nouvelle tentative en génère une autre.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: MediaFileConfiguration.StorageKeyIndex }
            ? new ConcurrencyException(FilesApplicationErrors.ConcurrentUpdate)
            : null;
}

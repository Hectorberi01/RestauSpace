using Catalog.Application.Queries;
using Files.Application.Abstractions;
using Files.Application.Commands;
using Files.Application.Queries;
using Files.Application.Storage;
using Files.Domain;
using Locations.Application.Queries;
using Reporting.Application.Exports;
using Reporting.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Files, servis par Tenants, Catalog et Locations, et le stockage des exports de Reporting servi
// par Files (Cloudflare R2). Aucun module n'en référence un autre : seul le host les relie.

/// <summary>Files -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class FileTenantsFromTenants(CachedTenants tenants) : IFileTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>
/// Files -> Catalog, Locations, Tenants : le propriétaire d'un fichier existe-t-il dans le tenant du contexte ? Les
/// requêtes des modules répondent pour le tenant du contexte seulement ; un autre tenant ou un type inconnu = non.
/// </summary>
internal sealed class FileOwnersFromModules(
    ITenantContext tenantContext,
    IQueryHandler<GetProductByIdQuery, ProductDto> products,
    IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IFileOwnerChecker
{
    public async Task<bool> BelongsToTenantAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        try
        {
            switch (owner.OwnerType)
            {
                case FileOwnerTypes.TenantsTenant:
                    // Le logo : seulement celui du restaurant lui-même.
                    return owner.OwnerId == tenantId.Value;
                case FileOwnerTypes.CatalogProduct:
                    _ = await products.HandleAsync(new GetProductByIdQuery(owner.OwnerId), cancellationToken);
                    return true;
                case FileOwnerTypes.LocationsLocation:
                    _ = await locations.HandleAsync(new GetLocationByIdQuery(owner.OwnerId), cancellationToken);
                    return true;
                default:
                    return false;
            }
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}

/// <summary>
/// Reporting -> Files : les exports CSV sont stockés par Files (propriétaire <c>reporting.export_job</c>), vérifiés comme
/// tout fichier, relus pour le téléchargement et supprimés à l'expiration de l'export. Agit pour le tenant du contexte,
/// qui doit être celui de l'export.
/// </summary>
internal sealed class ReportingExportFilesFromFiles(
    ITenantContext tenantContext,
    ICommandHandler<StoreGeneratedFileCommand, Guid> store,
    IQueryHandler<ReadFileContentQuery, FileContentDto> read,
    ICommandHandler<DeleteOwnedFileCommand, bool> delete) : Reporting.Application.Abstractions.IExportFileStore
{
    public async Task<ExportedFileReference> SaveAsync(
        TenantId tenantId, ExportJobId jobId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        var id = await store.HandleAsync(
            new StoreGeneratedFileCommand(FileOwnerTypes.ReportingExportJob, jobId.Value, fileName, contentType, content), cancellationToken);
        return new ExportedFileReference(tenantId, FileId.From(id));
    }

    public async Task<StoredExportFile?> OpenAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        try
        {
            var file = await read.HandleAsync(new ReadFileContentQuery(fileId.Value), cancellationToken);
            return new StoredExportFile(fileId, file.FileName, file.ContentType, file.Content);
        }
        catch (Exception exception) when (exception is NotFoundException or DomainException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default)
    {
        EnsureContext(tenantId);
        _ = await delete.HandleAsync(new DeleteOwnedFileCommand(fileId.Value), cancellationToken);
    }

    private void EnsureContext(TenantId tenantId)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            throw new InvalidOperationException("An export file can only be handled for the tenant of the current context.");
        }
    }
}

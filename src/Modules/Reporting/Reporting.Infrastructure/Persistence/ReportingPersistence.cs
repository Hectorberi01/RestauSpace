using Reporting.Application.Abstractions;
using Reporting.Application.Exports;
using Reporting.Application.Scheduling;
using Reporting.Domain;
using Reporting.Infrastructure.Persistence.Configurations;

namespace Reporting.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte. Les seules lectures sans filtre de
// tenant (IgnoreQueryFilters) sont celles des jobs (ReportingWorkQueue), et chacune le déclare.

internal sealed class ReportDefinitionRepository(ReportingDbContext context) : IReportDefinitionRepository
{
    public Task<ReportDefinition?> FindAsync(TenantId tenantId, ReportDefinitionId id, CancellationToken cancellationToken = default) =>
        context.Definitions.SingleOrDefaultAsync(definition => definition.TenantId == tenantId && definition.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ReportDefinition>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Definitions.AsNoTracking()
            .Where(definition => definition.TenantId == tenantId)
            .OrderBy(definition => definition.Name)
            .ThenBy(definition => definition.Id)
            .ToListAsync(cancellationToken);

    public void Add(ReportDefinition definition) => context.Definitions.Add(definition);
}

internal sealed class ExportJobRepository(ReportingDbContext context) : IExportJobRepository
{
    public Task<ExportJob?> FindAsync(TenantId tenantId, ExportJobId id, CancellationToken cancellationToken = default) =>
        context.Exports.SingleOrDefaultAsync(job => job.TenantId == tenantId && job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExportJob>> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Exports.AsNoTracking()
            .Where(job => job.TenantId == tenantId)
            .OrderByDescending(job => job.QueuedAtUtc)
            .ThenBy(job => job.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(ExportJob job) => context.Exports.Add(job);
}

internal sealed class ExportRequestStore(ReportingDbContext context) : IExportRequestStore
{
    public async Task<ExportRequest?> FindAsync(TenantId tenantId, ExportJobId jobId, CancellationToken cancellationToken = default)
    {
        var record = context.ExportRequests.Local.SingleOrDefault(candidate => candidate.TenantId == tenantId && candidate.ExportJobId == jobId)
            ?? await context.ExportRequests.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.ExportJobId == jobId, cancellationToken);
        return record is null ? null : ReportingConverters.ReadRequest(record.Parameters);
    }

    public void Add(TenantId tenantId, ExportJobId jobId, ExportRequest request) =>
        context.ExportRequests.Add(new ExportRequestRecord
        {
            ExportJobId = jobId,
            TenantId = tenantId,
            Parameters = ReportingConverters.WriteRequest(request),
        });
}

internal sealed class ReportDeliveryStore(ReportingDbContext context) : IReportDeliveryStore
{
    public Task<ReportDelivery?> FindAsync(TenantId tenantId, ReportDefinitionId definitionId, CancellationToken cancellationToken = default) =>
        context.Deliveries.SingleOrDefaultAsync(delivery => delivery.TenantId == tenantId && delivery.DefinitionId == definitionId, cancellationToken);

    public void Add(ReportDelivery delivery) => context.Deliveries.Add(delivery);

    public void Remove(ReportDelivery delivery) => context.Deliveries.Remove(delivery);
}

/// <summary>Les files du job : tous tenants, déclaré (IgnoreQueryFilters). Ne renvoie que des (tenant, id).</summary>
internal sealed class ReportingWorkQueue(ReportingDbContext context) : IReportingWorkQueue
{
    public async Task<IReadOnlyList<TenantWork>> ListDueDeliveriesAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Deliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(delivery => delivery.NextRunAtUtc <= nowUtc)
            .OrderBy(delivery => delivery.NextRunAtUtc)
            .Select(delivery => new { delivery.TenantId, delivery.DefinitionId })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new TenantWork(row.TenantId, row.DefinitionId.Value))
        .ToList();

    public async Task<IReadOnlyList<TenantWork>> ListQueuedExportsAsync(int take, CancellationToken cancellationToken = default) =>
        (await context.Exports.IgnoreQueryFilters().AsNoTracking()
            .Where(job => job.Status == ExportJobStatus.Queued)
            .OrderBy(job => job.QueuedAtUtc)
            .Select(job => new { job.TenantId, job.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new TenantWork(row.TenantId, row.Id.Value))
        .ToList();

    public async Task<IReadOnlyList<TenantWork>> ListExpiredExportsAsync(
        DateTimeOffset completedBeforeUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Exports.IgnoreQueryFilters().AsNoTracking()
            .Where(job => job.Status == ExportJobStatus.Completed && job.CompletedAtUtc <= completedBeforeUtc)
            .OrderBy(job => job.CompletedAtUtc)
            .Select(job => new { job.TenantId, job.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new TenantWork(row.TenantId, row.Id.Value))
        .ToList();
}

/// <summary>
/// Fichiers d'export en base, dans la transaction de l'unit of work (le fichier et le job terminé sont écrits ensemble).
/// À remplacer par le module Files quand il existera (même port).
/// </summary>
internal sealed class DatabaseExportFileStore(ReportingDbContext context, IClock clock) : IExportFileStore
{
    public Task<ExportedFileReference> SaveAsync(
        TenantId tenantId, ExportJobId jobId, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        var id = FileId.New();
        context.ExportFiles.Add(new StoredExportFileRecord
        {
            Id = id.Value,
            TenantId = tenantId,
            FileName = fileName,
            ContentType = contentType,
            Content = content,
            CreatedAtUtc = clock.UtcNow,
        });
        return Task.FromResult(new ExportedFileReference(tenantId, id));
    }

    public async Task<StoredExportFile?> OpenAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default)
    {
        var record = await context.ExportFiles.AsNoTracking()
            .SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == fileId.Value, cancellationToken);
        return record is null ? null : new StoredExportFile(fileId, record.FileName, record.ContentType, record.Content);
    }

    public async Task DeleteAsync(TenantId tenantId, FileId fileId, CancellationToken cancellationToken = default)
    {
        var record = await context.ExportFiles.SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == fileId.Value, cancellationToken);
        if (record is not null)
        {
            context.ExportFiles.Remove(record);
        }
    }
}

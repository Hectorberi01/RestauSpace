using Backups.Application;
using Backups.Application.Abstractions;
using Backups.Domain;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Backups.Infrastructure.Persistence;

internal sealed class BackupRepository(BackupsDbContext context) : IBackupRepository
{
    public Task<Backup?> FindAsync(BackupId id, CancellationToken cancellationToken = default) =>
        context.Backups.SingleOrDefaultAsync(backup => backup.Id == id, cancellationToken);

    public void Add(Backup backup) => context.Backups.Add(backup);

    public Task<bool> HasActiveAsync(CancellationToken cancellationToken = default) =>
        context.Backups.AnyAsync(
            backup => backup.Status == BackupStatus.Pending || backup.Status == BackupStatus.Running || backup.Status == BackupStatus.Uploading,
            cancellationToken);

    public Task<bool> HasScheduledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        context.Backups.AnyAsync(backup => backup.Trigger == BackupTrigger.Scheduled && backup.CreatedAtUtc >= sinceUtc, cancellationToken);

    public async Task<IReadOnlyList<Backup>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Backups.AsNoTracking()
            .OrderByDescending(backup => backup.CreatedAtUtc).ThenByDescending(backup => backup.Id)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => context.Backups.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Backup>> ListCompletedAsync(CancellationToken cancellationToken = default) =>
        await context.Backups
            .Where(backup => backup.Status == BackupStatus.Completed)
            .OrderByDescending(backup => backup.CreatedAtUtc).ThenByDescending(backup => backup.Id)
            .ToListAsync(cancellationToken);

    public Task<int> CountCompletedAsync(CancellationToken cancellationToken = default) =>
        context.Backups.CountAsync(backup => backup.Status == BackupStatus.Completed, cancellationToken);

    public async Task<IReadOnlyList<BackupId>> ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken = default) =>
        await context.Backups.AsNoTracking()
            .Where(backup => (backup.Status == BackupStatus.Running || backup.Status == BackupStatus.Uploading)
                && (backup.HeartbeatAtUtc ?? backup.StartedAtUtc ?? backup.CreatedAtUtc) < staleBeforeUtc)
            .Select(backup => backup.Id)
            .ToListAsync(cancellationToken);
}

internal sealed class RestoreRepository(BackupsDbContext context) : IRestoreRepository
{
    public Task<Restore?> FindAsync(RestoreId id, CancellationToken cancellationToken = default) =>
        context.Restores.SingleOrDefaultAsync(restore => restore.Id == id, cancellationToken);

    public void Add(Restore restore) => context.Restores.Add(restore);

    public Task<bool> HasActiveAsync(CancellationToken cancellationToken = default) =>
        context.Restores.AnyAsync(restore => restore.Status == RestoreStatus.Pending || restore.Status == RestoreStatus.Running, cancellationToken);

    public async Task<IReadOnlyList<Restore>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Restores.AsNoTracking()
            .OrderByDescending(restore => restore.CreatedAtUtc).ThenByDescending(restore => restore.Id)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => context.Restores.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<RestoreId>> ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken = default) =>
        await context.Restores.AsNoTracking()
            .Where(restore => restore.Status == RestoreStatus.Running
                && (restore.HeartbeatAtUtc ?? restore.StartedAtUtc ?? restore.CreatedAtUtc) < staleBeforeUtc)
            .Select(restore => restore.Id)
            .ToListAsync(cancellationToken);
}

internal sealed class BackupAgentReader(BackupsDbContext context) : IBackupAgentReader
{
    public async Task<AgentHeartbeat?> GetAsync(CancellationToken cancellationToken = default) =>
        await context.Agent.AsNoTracking()
            .Where(agent => agent.Id == 1)
            .Select(agent => new AgentHeartbeat(agent.Host, agent.Version, agent.LastSeenAtUtc, agent.RestoreEnabled))
            .SingleOrDefaultAsync(cancellationToken);
}

/// <summary>
/// Unit of work du module. Les index uniques partiels deviennent les erreurs métier ; après un refus, le suivi est vidé
/// (la passe suivante relit). Une transaction ouverte par <see cref="LockRetentionAsync"/> est validée à l'enregistrement.
/// </summary>
internal sealed class BackupsUnitOfWork(BackupsDbContext context) : IBackupsUnitOfWork
{
    private readonly Outbox _outbox = new(context);
    private IDbContextTransaction? _retention;

    public async Task LockRetentionAsync(CancellationToken cancellationToken = default)
    {
        if (_retention is not null)
        {
            return;
        }

        _retention = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('backups.retention', 0))", cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _outbox.SaveChangesAsync(cancellationToken);
            if (_retention is { } transaction)
            {
                _retention = null;
                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            if (_retention is { } transaction)
            {
                _retention = null;
                await transaction.DisposeAsync();
            }

            if (exception is ConcurrencyException or DomainException)
            {
                context.ChangeTracker.Clear();
            }

            throw;
        }
    }

    private sealed class Outbox(BackupsDbContext outboxContext) : OutboxUnitOfWork<BackupsDbContext>(outboxContext)
    {
        protected override DomainError ConcurrentUpdate => BackupsApplicationErrors.ConcurrentUpdate;

        protected override Exception? Translate(DbUpdateException exception) =>
            (exception.InnerException as PostgresException) switch
            {
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BackupConfiguration.ActiveIndex } =>
                    new DomainException(BackupErrors.AlreadyInProgress),
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RestoreConfiguration.ActiveIndex } =>
                    new DomainException(BackupErrors.RestoreInProgress),
                _ => null,
            };
    }
}

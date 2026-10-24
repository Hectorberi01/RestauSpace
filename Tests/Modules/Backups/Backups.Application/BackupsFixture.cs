using Backups.Application.Abstractions;
using Backups.Application.Commands;
using Backups.Application.Queries;
using Backups.Domain;

namespace Backups.Application;

/// <summary>
/// Ports en mémoire, avec les gardes de la base : une seule sauvegarde et une seule restauration actives (index uniques
/// partiels), concurrence optimiste simulée. Les événements enregistrés vont dans <see cref="Outbox"/>.
/// </summary>
public sealed class BackupsFixture
    : IBackupRepository, IRestoreRepository, IBackupAgentReader, IBackupStorage, IAdminTwoFactor, IBackupsUnitOfWork
{
    public const string ValidCode = "123456";

    private readonly List<Backup> _pendingBackups = [];
    private readonly List<Restore> _pendingRestores = [];

    public FakeClock Clock { get; } = new();

    public BackupsSettings Settings { get; } = new() { DatabaseName = "es_restaurant", KeepMin = 2 };

    public UserId Admin { get; } = UserId.New();

    public List<Backup> Backups { get; } = [];

    public List<Restore> Restores { get; } = [];

    public List<IDomainEvent> Outbox { get; } = [];

    public AgentHeartbeat? Agent { get; set; }

    public bool StorageConfigured { get; set; } = true;

    /// <summary>Simule une course : les vérifications ne voient pas la ligne active, seul l'index la voit.</summary>
    public bool HideActive { get; set; }

    /// <summary>Le prochain enregistrement échoue comme si l'agent avait écrit la ligne entre-temps (xmin).</summary>
    public int ConcurrentWrites { get; set; }

    public List<(Guid User, string? Code)> TwoFactorCalls { get; } = [];

    public Exception? TwoFactorFailure { get; set; }

    public int RetentionLocks { get; private set; }

    public int Saves { get; private set; }

    public RequestBackupHandler Request => new(this, this, this, Settings, Clock);

    public RunScheduledBackupHandler Schedule => new(this, this, this, Settings, Clock);

    public DeleteBackupHandler Delete => new(this, this, Settings, Clock);

    public CreateDownloadLinkHandler DownloadLink => new(this, this, this, this, Settings, Clock);

    public RequestRestoreHandler Restore => new(this, this, this, this, Settings, Clock);

    public CancelRestoreHandler Cancel => new(this, this, Clock);

    public RunMaintenanceHandler Maintenance => new(this, this, this, Settings, Clock);

    public ListBackupsHandler List => new(this);

    public GetBackupHandler Get => new(this);

    public ListRestoresHandler ListRestores => new(this);

    public GetBackupAgentHandler AgentStatus => new(this, Clock);

    public GetBackupSettingsHandler SettingsQuery => new(Settings, this);

    /// <summary>Une sauvegarde terminée par l'agent, créée à <paramref name="createdAtUtc"/> (maintenant par défaut).</summary>
    public Backup GivenCompleted(DateTimeOffset? createdAtUtc = null)
    {
        var at = createdAtUtc ?? Clock.UtcNow;
        var backup = Domain.Backup.RequestManual(Settings.DatabaseName, Admin, Settings.Retention, new FakeClock(at));
        backup.ClearDomainEvents();
        Backups.Add(AgentWrites.Completed(backup));
        return backup;
    }

    public Backup GivenActive()
    {
        var backup = Domain.Backup.RequestManual(Settings.DatabaseName, Admin, Settings.Retention, Clock);
        backup.ClearDomainEvents();
        Backups.Add(backup);
        return backup;
    }

    public Restore GivenPendingRestore()
    {
        var restore = Domain.Restore.Request(GivenCompleted(), Settings.DatabaseName, Settings.DatabaseName, Admin, Settings.RestoreDelay, Clock);
        restore.ClearDomainEvents();
        Restores.Add(restore);
        return restore;
    }

    public Task LockRetentionAsync(CancellationToken cancellationToken = default)
    {
        RetentionLocks++;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ConcurrentWrites > 0)
        {
            ConcurrentWrites--;
            throw new ConcurrencyException(BackupsApplicationErrors.ConcurrentUpdate);
        }

        if (Backups.Concat(_pendingBackups).Count(backup => backup.IsActive) > 1)
        {
            _pendingBackups.Clear();
            throw new DomainException(BackupErrors.AlreadyInProgress);
        }

        if (Restores.Concat(_pendingRestores).Count(restore => restore.IsActive) > 1)
        {
            _pendingRestores.Clear();
            throw new DomainException(BackupErrors.RestoreInProgress);
        }

        Backups.AddRange(_pendingBackups);
        Restores.AddRange(_pendingRestores);
        _pendingBackups.Clear();
        _pendingRestores.Clear();
        foreach (var aggregate in Backups.Cast<IAggregateRoot>().Concat(Restores))
        {
            Outbox.AddRange(aggregate.DomainEvents);
            aggregate.ClearDomainEvents();
        }

        Saves++;
        return Task.CompletedTask;
    }

    Task<Backup?> IBackupRepository.FindAsync(BackupId id, CancellationToken cancellationToken) =>
        Task.FromResult(Backups.SingleOrDefault(backup => backup.Id == id));

    void IBackupRepository.Add(Backup backup) => _pendingBackups.Add(backup);

    Task<bool> IBackupRepository.HasActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(!HideActive && Backups.Any(backup => backup.IsActive));

    Task<bool> IBackupRepository.HasScheduledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Backups.Any(backup => backup.Trigger == BackupTrigger.Scheduled && backup.CreatedAtUtc >= sinceUtc));

    Task<IReadOnlyList<Backup>> IBackupRepository.ListAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Backup>>(Backups.OrderByDescending(backup => backup.CreatedAtUtc).Skip(skip).Take(take).ToList());

    Task<int> IBackupRepository.CountAsync(CancellationToken cancellationToken) => Task.FromResult(Backups.Count);

    Task<IReadOnlyList<Backup>> IBackupRepository.ListCompletedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Backup>>(
            Backups.Where(backup => backup.Status == BackupStatus.Completed).OrderByDescending(backup => backup.CreatedAtUtc).ToList());

    Task<int> IBackupRepository.CountCompletedAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Backups.Count(backup => backup.Status == BackupStatus.Completed));

    Task<IReadOnlyList<BackupId>> IBackupRepository.ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BackupId>>(Backups
            .Where(backup => backup.Status is BackupStatus.Running or BackupStatus.Uploading
                && (backup.HeartbeatAtUtc ?? backup.StartedAtUtc ?? backup.CreatedAtUtc) < staleBeforeUtc)
            .Select(backup => backup.Id)
            .ToList());

    Task<Restore?> IRestoreRepository.FindAsync(RestoreId id, CancellationToken cancellationToken) =>
        Task.FromResult(Restores.SingleOrDefault(restore => restore.Id == id));

    void IRestoreRepository.Add(Restore restore) => _pendingRestores.Add(restore);

    Task<bool> IRestoreRepository.HasActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(!HideActive && Restores.Any(restore => restore.IsActive));

    Task<IReadOnlyList<Restore>> IRestoreRepository.ListAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Restore>>(Restores.OrderByDescending(restore => restore.CreatedAtUtc).Skip(skip).Take(take).ToList());

    Task<int> IRestoreRepository.CountAsync(CancellationToken cancellationToken) => Task.FromResult(Restores.Count);

    Task<IReadOnlyList<RestoreId>> IRestoreRepository.ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RestoreId>>(Restores
            .Where(restore => restore.Status == RestoreStatus.Running
                && (restore.HeartbeatAtUtc ?? restore.StartedAtUtc ?? restore.CreatedAtUtc) < staleBeforeUtc)
            .Select(restore => restore.Id)
            .ToList());

    Task<AgentHeartbeat?> IBackupAgentReader.GetAsync(CancellationToken cancellationToken) => Task.FromResult(Agent);

    bool IBackupStorage.IsConfigured => StorageConfigured;

    Uri IBackupStorage.PresignDownload(string storageKey, string fileName, TimeSpan lifetime) =>
        new($"https://storage.test/bucket/{storageKey}?X-Amz-Expires={(int)lifetime.TotalSeconds}&X-Amz-Signature=sig");

    Task<bool> IAdminTwoFactor.VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        TwoFactorCalls.Add((userId, code));
        return TwoFactorFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(code == ValidCode);
    }
}

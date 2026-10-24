namespace Backups.Domain;

/// <summary>Pending → Running → Completed | Failed ; Pending → Cancelled. Noms écrits en base (texte).</summary>
public enum RestoreStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Une demande de restauration d'une sauvegarde terminée. L'agent ne la prend qu'après <see cref="NotBeforeUtc"/> (délai
/// pour annuler) ; l'API ne pose que Cancelled et Failed(timeout).
/// </summary>
public sealed class Restore : AggregateRoot<RestoreId>
{
    private Restore()
    {
        DatabaseName = null!;
    }

    private Restore(RestoreId id, BackupId backupId, string databaseName, UserId requestedBy, DateTimeOffset nowUtc, TimeSpan delay)
        : base(id)
    {
        BackupId = backupId;
        DatabaseName = databaseName;
        Status = RestoreStatus.Pending;
        RequestedBy = requestedBy;
        CreatedAtUtc = nowUtc;
        NotBeforeUtc = nowUtc + delay;
    }

    public BackupId BackupId { get; private set; }

    public string DatabaseName { get; private set; }

    public RestoreStatus Status { get; private set; }

    public UserId RequestedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NotBeforeUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? HeartbeatAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? PreviousDatabaseName { get; private set; }

    public string? FailureCode { get; private set; }

    public bool IsActive => IsActiveStatus(Status);

    public static bool IsActiveStatus(RestoreStatus status) => status is RestoreStatus.Pending or RestoreStatus.Running;

    /// <summary>
    /// Restauration de <paramref name="backup"/> (Completed) dans <paramref name="databaseName"/>, que l'administrateur
    /// doit retaper à l'identique.
    /// </summary>
    public static Restore Request(
        Backup backup, string? databaseName, string? confirmDatabaseName, UserId requestedBy, TimeSpan delay, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        DomainException.ThrowIf(backup.Status != BackupStatus.Completed, BackupErrors.NotCompleted);
        var name = Backup.ValidDatabaseName(databaseName);
        DomainException.ThrowIf(!string.Equals(confirmDatabaseName?.Trim(), name, StringComparison.Ordinal), BackupErrors.ConfirmationMismatch);

        var restore = new Restore(RestoreId.New(), backup.Id, name, Guard.NotEmpty(requestedBy), clock.UtcNow, delay);
        restore.Raise(new RestoreRequested(restore.Id, backup.Id, name, requestedBy, restore.NotBeforeUtc, restore.CreatedAtUtc));
        return restore;
    }

    /// <summary>Pending seulement : l'agent ne l'a pas encore prise.</summary>
    public void Cancel(UserId cancelledBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != RestoreStatus.Pending, BackupErrors.NotCancellable);
        Status = RestoreStatus.Cancelled;
        CompletedAtUtc = clock.UtcNow;
        Raise(new RestoreCancelled(Id, BackupId, Guard.NotEmpty(cancelledBy), CompletedAtUtc.Value));
    }

    /// <summary>Running sans signe de vie depuis <paramref name="staleBeforeUtc"/> : Failed(timeout). Vrai si changée.</summary>
    public bool FailIfStale(DateTimeOffset staleBeforeUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status != RestoreStatus.Running || (HeartbeatAtUtc ?? StartedAtUtc ?? CreatedAtUtc) >= staleBeforeUtc)
        {
            return false;
        }

        Status = RestoreStatus.Failed;
        FailureCode = FailureCodes.Timeout;
        CompletedAtUtc = clock.UtcNow;
        return true;
    }
}

public sealed record RestoreRequested(
    RestoreId RestoreId, BackupId BackupId, string DatabaseName, UserId RequestedBy, DateTimeOffset NotBeforeUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RestoreCancelled(RestoreId RestoreId, BackupId BackupId, UserId CancelledBy, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

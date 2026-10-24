using System.Text.RegularExpressions;

namespace Backups.Domain;

public enum BackupTrigger
{
    Manual,
    Scheduled,
}

/// <summary>Pending → Running → Uploading → Completed ; Failed ; Completed → Deleting → Deleted. Noms écrits en base (texte).</summary>
public enum BackupStatus
{
    Pending,
    Running,
    Uploading,
    Completed,
    Failed,
    Deleting,
    Deleted,
}

/// <summary>
/// Une sauvegarde de la base. L'API crée la demande et ne pose que Failed(timeout) et Deleting ; l'agent du serveur de
/// base écrit le reste (Running, clé de stockage, taille, empreinte, Completed, Deleted).
/// </summary>
public sealed partial class Backup : AggregateRoot<BackupId>
{
    private Backup()
    {
        DatabaseName = null!;
    }

    private Backup(BackupId id, string databaseName, BackupTrigger trigger, UserId? requestedBy, DateTimeOffset nowUtc, TimeSpan retention)
        : base(id)
    {
        DatabaseName = databaseName;
        Trigger = trigger;
        Status = BackupStatus.Pending;
        RequestedBy = requestedBy;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc + retention;
    }

    public string DatabaseName { get; private set; }

    public BackupTrigger Trigger { get; private set; }

    public BackupStatus Status { get; private set; }

    public string? StorageKey { get; private set; }

    public long? SizeBytes { get; private set; }

    public string? ChecksumSha256 { get; private set; }

    /// <summary>Null : demande planifiée (système).</summary>
    public UserId? RequestedBy { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? HeartbeatAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public bool IsActive => IsActiveStatus(Status);

    public static bool IsActiveStatus(BackupStatus status) => status is BackupStatus.Pending or BackupStatus.Running or BackupStatus.Uploading;

    /// <summary>Nom de base PostgreSQL simple (pas de guillemets à prévoir).</summary>
    public static string ValidDatabaseName(string? databaseName)
    {
        var name = databaseName?.Trim() ?? string.Empty;
        DomainException.ThrowIf(!DatabaseNamePattern().IsMatch(name), BackupErrors.DatabaseNameInvalid);
        return name;
    }

    public static Backup RequestManual(string? databaseName, UserId requestedBy, TimeSpan retention, IClock clock) =>
        Create(databaseName, BackupTrigger.Manual, Guard.NotEmpty(requestedBy), retention, clock);

    public static Backup Schedule(string? databaseName, TimeSpan retention, IClock clock) =>
        Create(databaseName, BackupTrigger.Scheduled, null, retention, clock);

    private static Backup Create(string? databaseName, BackupTrigger trigger, UserId? requestedBy, TimeSpan retention, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        var backup = new Backup(BackupId.New(), ValidDatabaseName(databaseName), trigger, requestedBy, clock.UtcNow, retention);
        backup.Raise(new BackupRequested(backup.Id, backup.DatabaseName, trigger, requestedBy, backup.CreatedAtUtc));
        return backup;
    }

    /// <summary>Running ou Uploading sans signe de vie depuis <paramref name="staleBeforeUtc"/> : Failed(timeout). Vrai si changée.</summary>
    public bool FailIfStale(DateTimeOffset staleBeforeUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status is not (BackupStatus.Running or BackupStatus.Uploading) || (HeartbeatAtUtc ?? StartedAtUtc ?? CreatedAtUtc) >= staleBeforeUtc)
        {
            return false;
        }

        Status = BackupStatus.Failed;
        FailureCode = FailureCodes.Timeout;
        CompletedAtUtc = clock.UtcNow;
        return true;
    }

    /// <summary>Completed → Deleting (l'agent supprime le fichier). <paramref name="requestedBy"/> null : rétention automatique.</summary>
    public void RequestDeletion(UserId? requestedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != BackupStatus.Completed, BackupErrors.NotCompleted);
        Status = BackupStatus.Deleting;
        Raise(new BackupDeletionRequested(Id, requestedBy, clock.UtcNow));
    }

    /// <summary>Trace la création d'un lien de téléchargement (jamais l'URL).</summary>
    public void RecordDownloadLink(UserId requestedBy, DateTimeOffset expiresAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != BackupStatus.Completed, BackupErrors.NotCompleted);
        Raise(new BackupDownloadLinkCreated(Id, Guard.NotEmpty(requestedBy), expiresAtUtc, clock.UtcNow));
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseNamePattern();
}

public sealed record BackupRequested(
    BackupId BackupId, string DatabaseName, BackupTrigger Trigger, UserId? RequestedBy, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BackupDeletionRequested(BackupId BackupId, UserId? RequestedBy, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BackupDownloadLinkCreated(
    BackupId BackupId, UserId RequestedBy, DateTimeOffset ExpiresAtUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

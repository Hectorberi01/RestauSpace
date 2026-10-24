namespace Backups.Domain;

public static class BackupErrors
{
    public static readonly DomainError AlreadyInProgress =
        new("backups.backup.already_in_progress", "A backup is already pending or running.", DomainErrorKind.Conflict);

    public static readonly DomainError NotCompleted =
        new("backups.backup.not_completed", "This backup is not completed.", DomainErrorKind.Conflict);

    public static readonly DomainError KeepMinReached =
        new("backups.backup.keep_min_reached", "The minimum number of completed backups must be kept.", DomainErrorKind.Conflict);

    public static readonly DomainError DatabaseNameInvalid =
        new("backups.backup.database_name_invalid", "The database name must be a PostgreSQL identifier (lowercase letters, digits, '_').");

    public static readonly DomainError RestoreInProgress =
        new("backups.restore.in_progress", "A restore is already pending or running.", DomainErrorKind.Conflict);

    public static readonly DomainError ConfirmationMismatch =
        new("backups.restore.confirmation_mismatch", "The confirmation does not match the database name.");

    public static readonly DomainError NotCancellable =
        new("backups.restore.not_cancellable", "Only a pending restore can be cancelled.", DomainErrorKind.Conflict);
}

/// <summary>Codes d'échec : listes fermées, jamais de texte libre (ni la sortie de pg_dump).</summary>
public static class FailureCodes
{
    public const string Timeout = "timeout";

    public static IReadOnlyList<string> Backup { get; } =
        ["dump_failed", "verify_failed", "encrypt_failed", "upload_failed", Timeout, "agent_restarted"];

    public static IReadOnlyList<string> Restore { get; } =
        ["disabled", "download_failed", "checksum_mismatch", "invalid_key", "key_missing", "decrypt_failed", "restore_failed", "swap_failed", Timeout];
}

namespace Backups.Application;

/// <summary>Erreurs des cas d'usage de Backups (celles des règles métier vivent dans Backups.Domain).</summary>
public static class BackupsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("backups.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    public static readonly DomainError BackupNotFound = new("backups.backup.not_found", "The backup does not exist.");

    public static readonly DomainError RestoreNotFound = new("backups.restore.not_found", "The restore does not exist.");

    /// <summary>Code absent, faux, déjà utilisé, ou compte sans validation en deux étapes.</summary>
    public static readonly DomainError TwoFactorInvalid = new("backups.two_factor.invalid", "The two-factor code is invalid.");

    public static readonly DomainError StorageNotConfigured =
        new("backups.storage.not_configured", "The backup storage is not configured.", DomainErrorKind.Conflict);

    public static readonly DomainError InvalidAccessToken =
        new("backups.auth.invalid_access_token", "The access token does not identify a user.");
}

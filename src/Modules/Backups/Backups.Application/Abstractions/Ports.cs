using Backups.Domain;

namespace Backups.Application.Abstractions;

/// <summary>Une transaction sur le schéma <c>backups</c> ; les événements partent par son outbox (audit).</summary>
public interface IBackupsUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Ouvre une transaction et sérialise les décisions de rétention (suppression manuelle, purge) jusqu'au prochain
    /// enregistrement : <see cref="BackupsSettings.KeepMin"/> reste garanti à plusieurs instances.
    /// </summary>
    Task LockRetentionAsync(CancellationToken cancellationToken = default);
}

public interface IBackupRepository
{
    Task<Backup?> FindAsync(BackupId id, CancellationToken cancellationToken = default);

    void Add(Backup backup);

    /// <summary>Une sauvegarde Pending, Running ou Uploading existe-t-elle ?</summary>
    Task<bool> HasActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Une demande planifiée a-t-elle été créée depuis <paramref name="sinceUtc"/> ?</summary>
    Task<bool> HasScheduledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Les plus récentes d'abord, sans suivi.</summary>
    Task<IReadOnlyList<Backup>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Les Completed, les plus récentes d'abord, suivies (rétention).</summary>
    Task<IReadOnlyList<Backup>> ListCompletedAsync(CancellationToken cancellationToken = default);

    Task<int> CountCompletedAsync(CancellationToken cancellationToken = default);

    /// <summary>Ids des Running ou Uploading sans signe de vie depuis <paramref name="staleBeforeUtc"/>.</summary>
    Task<IReadOnlyList<BackupId>> ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken = default);
}

public interface IRestoreRepository
{
    Task<Restore?> FindAsync(RestoreId id, CancellationToken cancellationToken = default);

    void Add(Restore restore);

    /// <summary>Une restauration Pending ou Running existe-t-elle ?</summary>
    Task<bool> HasActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Restore>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Ids des Running sans signe de vie depuis <paramref name="staleBeforeUtc"/>.</summary>
    Task<IReadOnlyList<RestoreId>> ListStaleAsync(DateTimeOffset staleBeforeUtc, CancellationToken cancellationToken = default);
}

/// <summary>La ligne unique écrite par l'agent à chaque passage.</summary>
public sealed record AgentHeartbeat(string Host, string Version, DateTimeOffset LastSeenAtUtc, bool RestoreEnabled);

public interface IBackupAgentReader
{
    Task<AgentHeartbeat?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Stockage objet des sauvegardes, en LECTURE seule pour l'API (pré-signature).</summary>
public interface IBackupStorage
{
    bool IsConfigured { get; }

    /// <summary>URL GET pré-signée : ne jamais la journaliser.</summary>
    Uri PresignDownload(string storageKey, string fileName, TimeSpan lifetime);
}

/// <summary>Vérifie le code de validation en deux étapes d'un administrateur (fourni par l'hôte, via Identity).</summary>
public interface IAdminTwoFactor
{
    /// <summary>Faux si le code est faux ou si le compte n'a pas la 2FA ; trop d'essais : <see cref="TooManyRequestsException"/>.</summary>
    Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default);
}

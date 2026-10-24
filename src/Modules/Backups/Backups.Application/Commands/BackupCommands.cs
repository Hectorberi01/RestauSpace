using System.Text.RegularExpressions;
using Backups.Application.Abstractions;
using Backups.Application.Queries;
using Backups.Domain;

namespace Backups.Application.Commands;

/// <summary>Sauvegarde manuelle : une seule active à la fois, aucune pendant une restauration.</summary>
public sealed record RequestBackupCommand(Guid RequestedBy) : ICommand<BackupDto>;

public sealed class RequestBackupHandler(
    IBackupRepository backups, IRestoreRepository restores, IBackupsUnitOfWork unitOfWork, BackupsSettings settings, IClock clock)
    : ICommandHandler<RequestBackupCommand, BackupDto>
{
    public async Task<BackupDto> HandleAsync(RequestBackupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedBy = UserId.From(command.RequestedBy);
        DomainException.ThrowIf(await restores.HasActiveAsync(cancellationToken), BackupErrors.RestoreInProgress);
        DomainException.ThrowIf(await backups.HasActiveAsync(cancellationToken), BackupErrors.AlreadyInProgress);

        var backup = Backup.RequestManual(settings.DatabaseName, requestedBy, settings.Retention, clock);
        backups.Add(backup);
        // Une demande concurrente heurte l'index unique : même erreur (traduite par l'unit of work).
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BackupDto.Of(backup);
    }
}

/// <summary>
/// Demande planifiée du jour, si aucune n'a été créée depuis le dernier créneau. Rien (null) si une sauvegarde ou une
/// restauration est active : le worker réessaie au passage suivant. Plusieurs instances : l'index unique tranche.
/// </summary>
public sealed record RunScheduledBackupCommand : ICommand<BackupDto?>;

public sealed class RunScheduledBackupHandler(
    IBackupRepository backups, IRestoreRepository restores, IBackupsUnitOfWork unitOfWork, BackupsSettings settings, IClock clock)
    : ICommandHandler<RunScheduledBackupCommand, BackupDto?>
{
    public async Task<BackupDto?> HandleAsync(RunScheduledBackupCommand command, CancellationToken cancellationToken = default)
    {
        if (!settings.ScheduleEnabled
            || await backups.HasScheduledSinceAsync(settings.LastSlotAt(clock.UtcNow), cancellationToken)
            || await restores.HasActiveAsync(cancellationToken)
            || await backups.HasActiveAsync(cancellationToken))
        {
            return null;
        }

        var backup = Backup.Schedule(settings.DatabaseName, settings.Retention, clock);
        backups.Add(backup);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return BackupDto.Of(backup);
        }
        catch (DomainException exception) when (exception.Error == BackupErrors.AlreadyInProgress)
        {
            return null;
        }
    }
}

/// <summary>Completed → Deleting, si au moins KeepMin sauvegardes Completed restent.</summary>
public sealed record DeleteBackupCommand(Guid BackupId, Guid RequestedBy) : ICommand<BackupDto>;

public sealed class DeleteBackupHandler(IBackupRepository backups, IBackupsUnitOfWork unitOfWork, BackupsSettings settings, IClock clock)
    : ICommandHandler<DeleteBackupCommand, BackupDto>
{
    public async Task<BackupDto> HandleAsync(DeleteBackupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedBy = UserId.From(command.RequestedBy);
        await unitOfWork.LockRetentionAsync(cancellationToken);
        var backup = await BackupLookup.FindAsync(backups, command.BackupId, cancellationToken);
        DomainException.ThrowIf(backup.Status != BackupStatus.Completed, BackupErrors.NotCompleted);
        DomainException.ThrowIf(await backups.CountCompletedAsync(cancellationToken) - 1 < settings.KeepMin, BackupErrors.KeepMinReached);

        backup.RequestDeletion(requestedBy, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BackupDto.Of(backup);
    }
}

/// <summary>Lien de téléchargement (fichier toujours chiffré). La 2FA est vérifiée en dernier : un refus ne consomme pas le code.</summary>
public sealed record CreateDownloadLinkCommand(Guid BackupId, Guid RequestedBy, string? Code) : ICommand<BackupDownloadLinkDto>
{
    public override string ToString() => $"CreateDownloadLinkCommand ({BackupId}, {RequestedBy}, ***)";
}

/// <summary>Le lien pré-signé : à ne jamais journaliser.</summary>
public sealed record BackupDownloadLinkDto(string Url, DateTimeOffset ExpiresAtUtc)
{
    public override string ToString() => $"BackupDownloadLinkDto (***, {ExpiresAtUtc:O})";
}

public sealed class CreateDownloadLinkHandler(
    IBackupRepository backups,
    IBackupStorage storage,
    IAdminTwoFactor twoFactor,
    IBackupsUnitOfWork unitOfWork,
    BackupsSettings settings,
    IClock clock)
    : ICommandHandler<CreateDownloadLinkCommand, BackupDownloadLinkDto>
{
    public async Task<BackupDownloadLinkDto> HandleAsync(CreateDownloadLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedBy = UserId.From(command.RequestedBy);
        var backup = await BackupLookup.FindAsync(backups, command.BackupId, cancellationToken);
        DomainException.ThrowIf(!storage.IsConfigured, BackupsApplicationErrors.StorageNotConfigured);
        DomainException.ThrowIf(backup.Status != BackupStatus.Completed || !StorageKeys.IsValid(backup.StorageKey, backup.Id), BackupErrors.NotCompleted);
        await TwoFactor.EnsureAsync(twoFactor, requestedBy, command.Code, cancellationToken);

        var lifetime = settings.DownloadLinkLifetime;
        var expiresAtUtc = clock.UtcNow + lifetime;
        var url = storage.PresignDownload(backup.StorageKey!, $"{backup.DatabaseName}_{backup.Id}.dump.age", lifetime);
        backup.RecordDownloadLink(requestedBy, expiresAtUtc, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BackupDownloadLinkDto(url.AbsoluteUri, expiresAtUtc);
    }
}

/// <summary>
/// Restauration d'une sauvegarde Completed : nom de la base retapé, aucune sauvegarde ni restauration active, code 2FA.
/// L'agent ne la prend qu'après le délai d'annulation.
/// </summary>
public sealed record RequestRestoreCommand(Guid BackupId, Guid RequestedBy, string? Code, string? ConfirmDatabaseName) : ICommand<RestoreDto>
{
    public override string ToString() => $"RequestRestoreCommand ({BackupId}, {RequestedBy}, ***)";
}

public sealed class RequestRestoreHandler(
    IBackupRepository backups,
    IRestoreRepository restores,
    IAdminTwoFactor twoFactor,
    IBackupsUnitOfWork unitOfWork,
    BackupsSettings settings,
    IClock clock)
    : ICommandHandler<RequestRestoreCommand, RestoreDto>
{
    public async Task<RestoreDto> HandleAsync(RequestRestoreCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedBy = UserId.From(command.RequestedBy);
        var backup = await BackupLookup.FindAsync(backups, command.BackupId, cancellationToken);
        DomainException.ThrowIf(!StorageKeys.IsValid(backup.StorageKey, backup.Id), BackupErrors.NotCompleted);
        var restore = Restore.Request(backup, settings.DatabaseName, command.ConfirmDatabaseName, requestedBy, settings.RestoreDelay, clock);
        DomainException.ThrowIf(await restores.HasActiveAsync(cancellationToken), BackupErrors.RestoreInProgress);
        DomainException.ThrowIf(await backups.HasActiveAsync(cancellationToken), BackupErrors.AlreadyInProgress);
        await TwoFactor.EnsureAsync(twoFactor, requestedBy, command.Code, cancellationToken);

        restores.Add(restore);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RestoreDto.Of(restore);
    }
}

/// <summary>Annule une restauration encore Pending.</summary>
public sealed record CancelRestoreCommand(Guid RestoreId, Guid CancelledBy) : ICommand<RestoreDto>;

public sealed class CancelRestoreHandler(IRestoreRepository restores, IBackupsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CancelRestoreCommand, RestoreDto>
{
    public async Task<RestoreDto> HandleAsync(CancelRestoreCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var cancelledBy = UserId.From(command.CancelledBy);
        var restore = (command.RestoreId == Guid.Empty ? null : await restores.FindAsync(RestoreId.From(command.RestoreId), cancellationToken))
            ?? throw new NotFoundException(BackupsApplicationErrors.RestoreNotFound);
        restore.Cancel(cancelledBy, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RestoreDto.Of(restore);
    }
}

public sealed record MaintenanceReport(int BackupsTimedOut, int RestoresTimedOut, int BackupsExpired);

/// <summary>
/// Maintenance : travaux sans heartbeat → Failed(timeout) ; sauvegardes Completed expirées → Deleting, en gardant
/// toujours les KeepMin plus récentes.
/// </summary>
public sealed record RunMaintenanceCommand : ICommand<MaintenanceReport>;

public sealed class RunMaintenanceHandler(
    IBackupRepository backups, IRestoreRepository restores, IBackupsUnitOfWork unitOfWork, BackupsSettings settings, IClock clock)
    : ICommandHandler<RunMaintenanceCommand, MaintenanceReport>
{
    public async Task<MaintenanceReport> HandleAsync(RunMaintenanceCommand command, CancellationToken cancellationToken = default)
    {
        var staleBefore = clock.UtcNow - settings.StaleAfter;
        var backupsTimedOut = 0;
        foreach (var id in await backups.ListStaleAsync(staleBefore, cancellationToken))
        {
            // Une ligne à la fois : un heartbeat de l'agent entre-temps (xmin) annule seulement celle-ci.
            if (await backups.FindAsync(id, cancellationToken) is { } backup && backup.FailIfStale(staleBefore, clock)
                && await TrySaveAsync(cancellationToken))
            {
                backupsTimedOut++;
            }
        }

        var restoresTimedOut = 0;
        foreach (var id in await restores.ListStaleAsync(staleBefore, cancellationToken))
        {
            if (await restores.FindAsync(id, cancellationToken) is { } restore && restore.FailIfStale(staleBefore, clock)
                && await TrySaveAsync(cancellationToken))
            {
                restoresTimedOut++;
            }
        }

        await unitOfWork.LockRetentionAsync(cancellationToken);
        var expired = (await backups.ListCompletedAsync(cancellationToken))
            .Skip(Math.Max(0, settings.KeepMin))
            .Where(backup => backup.ExpiresAtUtc <= clock.UtcNow)
            .ToList();
        expired.ForEach(backup => backup.RequestDeletion(null, clock));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MaintenanceReport(backupsTimedOut, restoresTimedOut, expired.Count);
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyException)
        {
            return false;
        }
    }
}

internal static class BackupLookup
{
    public static async Task<Backup> FindAsync(IBackupRepository backups, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await backups.FindAsync(BackupId.From(id), cancellationToken))
        ?? throw new NotFoundException(BackupsApplicationErrors.BackupNotFound);
}

internal static class TwoFactor
{
    public static async Task EnsureAsync(IAdminTwoFactor twoFactor, UserId user, string? code, CancellationToken cancellationToken)
    {
        var value = code?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(value) || value.Length > 64 || !await twoFactor.VerifyAsync(user.Value, value, cancellationToken),
            BackupsApplicationErrors.TwoFactorInvalid);
    }
}

/// <summary>Clé écrite par l'agent : <c>{base}/{yyyy}/{MM}/{id}.dump.age</c>. Revérifiée avant toute pré-signature.</summary>
public static partial class StorageKeys
{
    public static bool IsValid(string? storageKey, BackupId backupId) =>
        storageKey is not null && Pattern().IsMatch(storageKey) && storageKey.EndsWith("/" + backupId + ".dump.age", StringComparison.Ordinal);

    [GeneratedRegex(@"^[a-z0-9_]+/[0-9]{4}/[0-9]{2}/[0-9a-f-]{36}\.dump\.age$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

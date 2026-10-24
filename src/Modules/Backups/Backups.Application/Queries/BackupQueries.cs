using Backups.Application.Abstractions;
using Backups.Domain;

namespace Backups.Application.Queries;

/// <summary>Une sauvegarde telle que montrée (jamais la clé de stockage).</summary>
public sealed record BackupDto(
    Guid Id,
    string DatabaseName,
    string Trigger,
    string Status,
    long? SizeBytes,
    string? ChecksumSha256,
    Guid? RequestedBy,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ExpiresAtUtc)
{
    public static BackupDto Of(Backup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return new(
            backup.Id.Value,
            backup.DatabaseName,
            backup.Trigger.ToString(),
            backup.Status.ToString(),
            backup.SizeBytes,
            backup.ChecksumSha256,
            backup.RequestedBy?.Value,
            backup.FailureCode,
            backup.CreatedAtUtc,
            backup.StartedAtUtc,
            backup.CompletedAtUtc,
            backup.ExpiresAtUtc);
    }
}

public sealed record RestoreDto(
    Guid Id,
    Guid BackupId,
    string DatabaseName,
    string Status,
    Guid RequestedBy,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? PreviousDatabaseName)
{
    public static RestoreDto Of(Restore restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        return new(
            restore.Id.Value,
            restore.BackupId.Value,
            restore.DatabaseName,
            restore.Status.ToString(),
            restore.RequestedBy.Value,
            restore.FailureCode,
            restore.CreatedAtUtc,
            restore.NotBeforeUtc,
            restore.StartedAtUtc,
            restore.CompletedAtUtc,
            restore.PreviousDatabaseName);
    }
}

/// <summary>Agent jamais vu : tout à null et <see cref="Online"/> faux.</summary>
public sealed record BackupAgentDto(string? Host, string? Version, DateTimeOffset? LastSeenAtUtc, bool? RestoreEnabled, bool Online);

/// <param name="DailyAtUtc">« HH:mm ».</param>
public sealed record BackupSettingsDto(
    string DatabaseName, int RetentionDays, int KeepMin, string DailyAtUtc, int RestoreDelayMinutes, bool StorageConfigured);

public sealed record ListBackupsQuery(int Page, int PageSize) : IQuery<PagedResult<BackupDto>>;

public sealed class ListBackupsHandler(IBackupRepository backups) : IQueryHandler<ListBackupsQuery, PagedResult<BackupDto>>
{
    public async Task<PagedResult<BackupDto>> HandleAsync(ListBackupsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = await backups.ListAsync((query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        var total = await backups.CountAsync(cancellationToken);
        return PagedResult<BackupDto>.Counted(items.Select(BackupDto.Of).ToList(), query.Page, query.PageSize, total);
    }
}

public sealed record GetBackupQuery(Guid BackupId) : IQuery<BackupDto>;

public sealed class GetBackupHandler(IBackupRepository backups) : IQueryHandler<GetBackupQuery, BackupDto>
{
    public async Task<BackupDto> HandleAsync(GetBackupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var backup = query.BackupId == Guid.Empty ? null : await backups.FindAsync(BackupId.From(query.BackupId), cancellationToken);
        return BackupDto.Of(backup ?? throw new NotFoundException(BackupsApplicationErrors.BackupNotFound));
    }
}

public sealed record ListRestoresQuery(int Page, int PageSize) : IQuery<PagedResult<RestoreDto>>;

public sealed class ListRestoresHandler(IRestoreRepository restores) : IQueryHandler<ListRestoresQuery, PagedResult<RestoreDto>>
{
    public async Task<PagedResult<RestoreDto>> HandleAsync(ListRestoresQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = await restores.ListAsync((query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        var total = await restores.CountAsync(cancellationToken);
        return PagedResult<RestoreDto>.Counted(items.Select(RestoreDto.Of).ToList(), query.Page, query.PageSize, total);
    }
}

public sealed record GetBackupAgentQuery : IQuery<BackupAgentDto>;

public sealed class GetBackupAgentHandler(IBackupAgentReader agent, IClock clock) : IQueryHandler<GetBackupAgentQuery, BackupAgentDto>
{
    public async Task<BackupAgentDto> HandleAsync(GetBackupAgentQuery query, CancellationToken cancellationToken = default) =>
        await agent.GetAsync(cancellationToken) is { } seen
            ? new BackupAgentDto(seen.Host, seen.Version, seen.LastSeenAtUtc, seen.RestoreEnabled, clock.UtcNow - seen.LastSeenAtUtc < BackupsSettings.AgentOnlineWindow)
            : new BackupAgentDto(null, null, null, null, false);
}

public sealed record GetBackupSettingsQuery : IQuery<BackupSettingsDto>;

public sealed class GetBackupSettingsHandler(BackupsSettings settings, IBackupStorage storage) : IQueryHandler<GetBackupSettingsQuery, BackupSettingsDto>
{
    public Task<BackupSettingsDto> HandleAsync(GetBackupSettingsQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BackupSettingsDto(
            settings.DatabaseName,
            settings.RetentionDays,
            settings.KeepMin,
            settings.DailyAtUtc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            settings.RestoreDelayMinutes,
            storage.IsConfigured));
}

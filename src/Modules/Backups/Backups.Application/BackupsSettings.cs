namespace Backups.Application;

/// <summary>Réglages des cas d'usage (section <c>Backups</c> de l'hôte). Aucun secret ici.</summary>
public sealed class BackupsSettings
{
    /// <summary>Base sauvegardée ; à retaper pour confirmer une restauration.</summary>
    public string DatabaseName { get; set; } = string.Empty;

    public int RetentionDays { get; set; } = 30;

    /// <summary>Sauvegardes Completed toujours gardées, même expirées.</summary>
    public int KeepMin { get; set; } = 7;

    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>Heure UTC de la sauvegarde planifiée quotidienne.</summary>
    public TimeOnly DailyAtUtc { get; set; } = new(2, 30);

    /// <summary>Délai entre la demande de restauration et sa prise par l'agent (pour annuler).</summary>
    public int RestoreDelayMinutes { get; set; } = 5;

    /// <summary>Running ou Uploading sans heartbeat depuis ce délai : Failed(timeout).</summary>
    public int StaleAfterMinutes { get; set; } = 60;

    public int DownloadLinkMinutes { get; set; } = 5;

    /// <summary>Agent vu depuis moins de ce délai : en ligne.</summary>
    public static readonly TimeSpan AgentOnlineWindow = TimeSpan.FromMinutes(3);

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);

    public TimeSpan RestoreDelay => TimeSpan.FromMinutes(RestoreDelayMinutes);

    public TimeSpan StaleAfter => TimeSpan.FromMinutes(StaleAfterMinutes);

    public TimeSpan DownloadLinkLifetime => TimeSpan.FromMinutes(DownloadLinkMinutes);

    /// <summary>Refuse au démarrage une configuration incohérente.</summary>
    public void Validate()
    {
        _ = Backups.Domain.Backup.ValidDatabaseName(DatabaseName);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(KeepMin, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(RestoreDelayMinutes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(StaleAfterMinutes, 1);
        // Une URL pré-signée SigV4 vit 7 jours au plus.
        ArgumentOutOfRangeException.ThrowIfLessThan(DownloadLinkMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DownloadLinkMinutes, 60);
    }

    /// <summary>Dernier créneau planifié atteint à <paramref name="nowUtc"/> (aujourd'hui ou hier).</summary>
    public DateTimeOffset LastSlotAt(DateTimeOffset nowUtc)
    {
        var now = nowUtc.ToUniversalTime();
        var today = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(DailyAtUtc), TimeSpan.Zero);
        return today <= now ? today : today.AddDays(-1);
    }
}

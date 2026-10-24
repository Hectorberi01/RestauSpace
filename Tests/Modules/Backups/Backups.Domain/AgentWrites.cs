namespace Backups.Domain;

/// <summary>Ce que l'agent du serveur de base écrit en SQL (Running, heartbeat, Completed...), simulé par réflexion.</summary>
public static class AgentWrites
{
    public static T Set<T>(T row, string property, object? value)
        where T : class
    {
        typeof(T).GetProperty(property)!.SetValue(row, value);
        return row;
    }

    public static Backup Running(Backup backup, DateTimeOffset startedAtUtc, DateTimeOffset? heartbeatAtUtc = null)
    {
        Set(backup, nameof(Backup.Status), BackupStatus.Running);
        Set(backup, nameof(Backup.StartedAtUtc), startedAtUtc);
        return Set(backup, nameof(Backup.HeartbeatAtUtc), heartbeatAtUtc);
    }

    public static Backup Completed(Backup backup, DateTimeOffset? completedAtUtc = null)
    {
        Set(backup, nameof(Backup.Status), BackupStatus.Completed);
        Set(backup, nameof(Backup.StorageKey), $"{backup.DatabaseName}/2026/01/{backup.Id}.dump.age");
        Set(backup, nameof(Backup.SizeBytes), 1024L);
        Set(backup, nameof(Backup.ChecksumSha256), new string('a', 64));
        return Set(backup, nameof(Backup.CompletedAtUtc), completedAtUtc ?? backup.CreatedAtUtc);
    }

    public static Restore Running(Restore restore, DateTimeOffset startedAtUtc, DateTimeOffset? heartbeatAtUtc = null)
    {
        Set(restore, nameof(Restore.Status), RestoreStatus.Running);
        Set(restore, nameof(Restore.StartedAtUtc), startedAtUtc);
        return Set(restore, nameof(Restore.HeartbeatAtUtc), heartbeatAtUtc);
    }
}

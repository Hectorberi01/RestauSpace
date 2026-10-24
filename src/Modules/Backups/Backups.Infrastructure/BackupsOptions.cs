namespace Backups.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Backups</c> de l'hôte).</summary>
public sealed class BackupsOptions
{
    /// <summary>Faux : planification et maintenance ne tournent pas seules (tests, outils).</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan ScheduleInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);

    public BackupStorageOptions Storage { get; set; } = new();
}

/// <summary>Stockage objet compatible S3 (OVH : https://s3.gra.io.cloud.ovh.net, région gra). Clé en LECTURE seule.</summary>
public sealed class BackupStorageOptions
{
    public string? Endpoint { get; set; }

    public string Region { get; set; } = "gra";

    public string? Bucket { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(Region) && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);

    public override string ToString() => $"BackupStorageOptions ({Endpoint}, {Region}, {Bucket}, ***)";
}

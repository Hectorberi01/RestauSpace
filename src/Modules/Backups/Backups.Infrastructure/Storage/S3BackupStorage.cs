using Backups.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Backups.Infrastructure.Storage;

/// <summary>
/// Pré-signature GET (SigV4, adressage par chemin <c>{endpoint}/{bucket}/{clé}</c>). Aucun appel réseau ; la clé secrète
/// ne sort pas du signataire.
/// </summary>
internal sealed class S3BackupStorage(BackupStorageOptions options, IClock clock) : IBackupStorage
{
    public bool IsConfigured => options.IsConfigured;

    public Uri PresignDownload(string storageKey, string fileName, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Backup storage is not configured.");
        }

        var signer = new SigV4(options.AccessKeyId!.Trim(), options.SecretAccessKey!.Trim(), options.Region.Trim());
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response-content-disposition"] = $"attachment; filename=\"{Ascii(fileName)}\"",
        };
        return signer.Presign(
            "GET", new Uri(options.Endpoint!), SigV4.CanonicalPath(options.Bucket!.Trim(), storageKey), new Dictionary<string, string>(), query, lifetime, clock.UtcNow);
    }

    private static string Ascii(string fileName) =>
        new(fileName.Select(c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
}

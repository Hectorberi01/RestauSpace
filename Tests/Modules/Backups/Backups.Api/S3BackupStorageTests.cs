using System.Web;
using Backups.Infrastructure;
using Backups.Infrastructure.Storage;

namespace Backups.Api;

/// <summary>Pré-signature locale (aucun appel réseau) : format de l'URL et absence de la clé secrète.</summary>
public sealed class S3BackupStorageTests
{
    private const string Key = "es_restaurant/2026/10/01928f3e-7c1a-7b2e-9f00-1234567890ab.dump.age";
    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero));

    private static BackupStorageOptions Options(string secret = "ovh-read-only-secret") => new()
    {
        Endpoint = "https://s3.gra.io.cloud.ovh.net",
        Region = "gra",
        Bucket = "restauspace-backups",
        AccessKeyId = "OVHREADKEY",
        SecretAccessKey = secret,
    };

    [Fact]
    public void The_url_is_a_path_style_sigv4_get_for_the_gra_region()
    {
        var url = new S3BackupStorage(Options(), Clock).PresignDownload(Key, "es_restaurant.dump.age", TimeSpan.FromMinutes(5));
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://s3.gra.io.cloud.ovh.net/restauspace-backups/" + Key, url.GetLeftPart(UriPartial.Path));
        Assert.Equal("OVHREADKEY/20261025/gra/s3/aws4_request", query["X-Amz-Credential"]);
        Assert.Equal("20261025T080000Z", query["X-Amz-Date"]);
        Assert.Equal("300", query["X-Amz-Expires"]);
        Assert.Equal("attachment; filename=\"es_restaurant.dump.age\"", query["response-content-disposition"]);
        Assert.Matches("^[0-9a-f]{64}$", query["X-Amz-Signature"]);
        Assert.DoesNotContain("ovh-read-only-secret", url.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void The_signature_depends_on_the_secret()
    {
        var one = new S3BackupStorage(Options(), Clock).PresignDownload(Key, "a.dump.age", TimeSpan.FromMinutes(5));
        var other = new S3BackupStorage(Options("another-secret"), Clock).PresignDownload(Key, "a.dump.age", TimeSpan.FromMinutes(5));

        Assert.NotEqual(HttpUtility.ParseQueryString(one.Query)["X-Amz-Signature"], HttpUtility.ParseQueryString(other.Query)["X-Amz-Signature"]);
    }

    [Fact]
    public void A_storage_without_credentials_or_https_is_not_configured()
    {
        Assert.True(Options().IsConfigured);
        Assert.False(new BackupStorageOptions().IsConfigured);
        var plain = Options();
        plain.Endpoint = "http://s3.gra.io.cloud.ovh.net";
        Assert.False(plain.IsConfigured);
        Assert.DoesNotContain("ovh-read-only-secret", Options().ToString(), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new S3BackupStorage(new BackupStorageOptions(), Clock).PresignDownload(Key, "a", TimeSpan.FromMinutes(5)));
    }
}

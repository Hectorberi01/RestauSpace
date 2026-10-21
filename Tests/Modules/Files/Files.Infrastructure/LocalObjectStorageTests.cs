using System.Text;
using Files.Domain;
using Files.Infrastructure.Storage;

namespace Files.Infrastructure;

/// <summary>Stockage local de développement : URLs signées, refus des jetons falsifiés ou expirés, octets sur disque.</summary>
public sealed class LocalObjectStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"files-{Guid.NewGuid():N}");
    private readonly FakeClock _clock = new();
    private readonly LocalObjectStorage _storage;
    private readonly StorageKey _key = StorageKey.Create($"tenants/{Guid.NewGuid():N}/catalog.product/{Guid.NewGuid():N}");

    public LocalObjectStorageTests()
    {
        var options = new LocalStorageOptions
        {
            Directory = _directory,
            PublicBaseUrl = new Uri("http://localhost:5080"),
            SigningKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray()),
        };
        _storage = new LocalObjectStorage(options, _clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string TokenOf(Uri url) => url.AbsolutePath[LocalObjectStorage.RoutePrefix.Length..];

    [Fact]
    public void An_upload_url_grants_exactly_the_declared_put()
    {
        var url = _storage.PresignUpload(_key, ContentType.Create("image/png"), 1234, TimeSpan.FromMinutes(15));

        Assert.StartsWith("http://localhost:5080/v1/files/local/", url.AbsoluteUri, StringComparison.Ordinal);
        var grant = _storage.Verify(TokenOf(url), "PUT");
        Assert.NotNull(grant);
        Assert.Equal(_key, grant.Key);
        Assert.Equal("image/png", grant.ContentType);
        Assert.Equal(1234, grant.SizeInBytes);
        Assert.Null(_storage.Verify(TokenOf(url), "GET"));
    }

    [Fact]
    public void A_tampered_or_expired_token_grants_nothing()
    {
        var url = _storage.PresignDownload(_key, FileName.Create("photo.png"), TimeSpan.FromMinutes(5));
        var token = TokenOf(url);
        var dot = token.IndexOf('.', StringComparison.Ordinal);
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        Assert.NotNull(_storage.Verify(token, "GET"));
        Assert.Null(_storage.Verify(tampered, "GET"));
        Assert.Null(_storage.Verify(token[..dot], "GET"));
        Assert.Null(_storage.Verify("not-a-token", "GET"));

        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(_storage.Verify(token, "GET"));
    }

    [Fact]
    public async Task Bytes_are_written_under_a_hashed_name_then_inspected_read_and_deleted()
    {
        var content = Encoding.ASCII.GetBytes("0123456789");

        await _storage.WriteAsync(_key, content);
        var stored = await _storage.InspectAsync(_key, 4);

        Assert.NotNull(stored);
        Assert.Equal(10, stored.SizeInBytes);
        Assert.Equal(Encoding.ASCII.GetBytes("0123"), stored.Head);
        Assert.Equal(content, await _storage.GetAsync(_key));
        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches("^[0-9a-f]{64}$", Path.GetFileName(file));

        await _storage.DeleteAsync(_key);
        await _storage.DeleteAsync(_key);
        Assert.Null(await _storage.InspectAsync(_key, 4));
    }

    [Fact]
    public void Incomplete_settings_are_refused_without_quoting_the_key()
    {
        var options = new LocalStorageOptions { Directory = _directory, PublicBaseUrl = new Uri("http://localhost:5080"), SigningKey = "c2hvcnQ=" };

        var error = Assert.Throws<InvalidOperationException>(() => new LocalObjectStorage(options, _clock));
        Assert.DoesNotContain("c2hvcnQ=", error.Message, StringComparison.Ordinal);
    }
}

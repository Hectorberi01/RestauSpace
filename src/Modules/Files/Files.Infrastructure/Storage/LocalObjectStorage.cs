using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Files.Application.Abstractions;
using Files.Domain;

namespace Files.Infrastructure.Storage;

/// <summary>Ce qu'une URL signée du stockage local autorise : une méthode sur un objet, jusqu'à une échéance.</summary>
public sealed record LocalGrant(string Method, StorageKey Key, string? ContentType, long? SizeInBytes, string? FileName);

/// <summary>
/// Stockage sur le disque de l'API, pour le DÉVELOPPEMENT : le navigateur envoie (PUT) et lit (GET) les octets sur
/// <c>/v1/files/local/{jeton}</c>, comme il le ferait avec les URLs pré-signées de R2. Le jeton porte la méthode, la clé,
/// le type et la taille exacts et une échéance, signés en HMAC-SHA256 ; sans signature valide rien n'est lu ni écrit.
/// Le nom du fichier sur disque est l'empreinte SHA-256 de la clé : aucune partie de la clé ne devient un chemin.
/// </summary>
public sealed class LocalObjectStorage : IObjectStorage
{
    public const string RoutePrefix = "/v1/files/local/";

    private readonly string _directory;
    private readonly Uri _publicBaseUrl;
    private readonly byte[] _signingKey;
    private readonly IClock _clock;

    public LocalObjectStorage(LocalStorageOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _signingKey = options.Validate();
        _directory = Path.GetFullPath(options.Directory);
        _publicBaseUrl = options.PublicBaseUrl!;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        System.IO.Directory.CreateDirectory(_directory);
    }

    public Uri PresignUpload(StorageKey key, ContentType contentType, long sizeInBytes, TimeSpan lifetime) =>
        Sign(new Token("PUT", key.Value, contentType.Value, sizeInBytes, null, Expiry(lifetime)));

    public Uri PresignDownload(StorageKey key, FileName fileName, TimeSpan lifetime) =>
        Sign(new Token("GET", key.Value, null, null, fileName.Value, Expiry(lifetime)));

    /// <summary>Le droit porté par <paramref name="token"/> pour <paramref name="method"/> ; null s'il est faux ou expiré.</summary>
    public LocalGrant? Verify(string? token, string method)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_signingKey, payload)))
        {
            return null;
        }

        Token? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<Token>(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        if (decoded is null || decoded.M != method || decoded.E < _clock.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        return new LocalGrant(decoded.M, StorageKey.Create(decoded.K), decoded.T, decoded.S, decoded.N);
    }

    public async Task<StoredObject?> InspectAsync(StorageKey key, int headLength, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var head = new byte[Math.Min(headLength, stream.Length)];
        await stream.ReadExactlyAsync(head, cancellationToken);
        return new StoredObject(stream.Length, head);
    }

    public Task PutAsync(StorageKey key, ContentType contentType, byte[] content, CancellationToken cancellationToken = default) =>
        WriteAsync(key, content, cancellationToken);

    /// <summary>Écrit l'objet en entier (fichier temporaire puis renommage : jamais d'objet à moitié écrit).</summary>
    public async Task WriteAsync(StorageKey key, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(key);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<byte[]?> GetAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    public Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    private string PathOf(StorageKey key) =>
        Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.Value))));

    private long Expiry(TimeSpan lifetime) => (_clock.UtcNow + lifetime).ToUnixTimeSeconds();

    private Uri Sign(Token token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(token);
        var signed = $"{Base64UrlEncode(payload)}.{Base64UrlEncode(HMACSHA256.HashData(_signingKey, payload))}";
        return new Uri(_publicBaseUrl, RoutePrefix + signed);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }

    // Noms courts : le jeton voyage dans l'URL.
    private sealed record Token(string M, string K, string? T, long? S, string? N, long E);
}

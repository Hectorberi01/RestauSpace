using System.Buffers.Text;
using System.Security.Cryptography;
using Files.Domain;

namespace Files.Application.Storage;

/// <summary>Le fichier désigné par un lien public vérifié.</summary>
public sealed record PublicMediaReference(TenantId TenantId, FileId FileId);

/// <summary>
/// Liens publics STABLES des photos affichées aux clients (catalogue en ligne, borne) : <c>{base}/v1/media/{jeton}</c>.
/// Le jeton porte le tenant et le fichier, signés en HMAC-SHA256 : il ne s'invente pas et ne se modifie pas, et il ne
/// change jamais pour un même fichier (cache navigateur et CDN). Un fichier n'est jamais réécrit : une nouvelle photo
/// est un nouveau fichier, donc un nouveau lien. Sans clé configurée, aucun lien n'est produit ni accepté.
/// </summary>
public sealed class PublicMediaLinks
{
    public const string RoutePrefix = "/v1/media/";
    private const int IdLength = 16;
    private const int SignatureLength = 16;
    private static readonly byte[] Purpose = "files.public-media.v1"u8.ToArray();

    private readonly Uri? _baseUrl;
    private readonly byte[]? _key;

    /// <param name="baseUrl">Adresse publique de l'API (ou du CDN placé devant elle).</param>
    /// <param name="key">Clé de signature, 32 octets au moins ; null = liens publics désactivés.</param>
    public PublicMediaLinks(Uri? baseUrl, byte[]? key)
    {
        if (key is not null && (key.Length < 32 || baseUrl is not { IsAbsoluteUri: true }))
        {
            throw new InvalidOperationException("Public media links need an absolute base URL and a signing key of at least 32 bytes.");
        }

        _baseUrl = baseUrl;
        _key = key;
    }

    public static PublicMediaLinks Disabled { get; } = new(null, null);

    public bool IsEnabled => _key is not null;

    /// <summary>Le lien public du fichier ; null si les liens publics ne sont pas configurés.</summary>
    public Uri? UrlFor(TenantId tenantId, FileId fileId)
    {
        if (_key is null)
        {
            return null;
        }

        var token = new byte[IdLength * 2 + SignatureLength];
        tenantId.Value.TryWriteBytes(token.AsSpan(0, IdLength), bigEndian: true, out _);
        fileId.Value.TryWriteBytes(token.AsSpan(IdLength, IdLength), bigEndian: true, out _);
        Sign(token.AsSpan(0, IdLength * 2)).AsSpan(0, SignatureLength).CopyTo(token.AsSpan(IdLength * 2));
        return new Uri(_baseUrl!, RoutePrefix + Base64Url.EncodeToString(token));
    }

    /// <summary>Le fichier désigné par <paramref name="token"/> ; null si le jeton est faux ou les liens désactivés.</summary>
    public PublicMediaReference? Read(string? token)
    {
        if (_key is null || string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length != IdLength * 2 + SignatureLength
            || !CryptographicOperations.FixedTimeEquals(Sign(bytes.AsSpan(0, IdLength * 2)).AsSpan(0, SignatureLength), bytes.AsSpan(IdLength * 2)))
        {
            return null;
        }

        var tenant = new Guid(bytes.AsSpan(0, IdLength), bigEndian: true);
        var file = new Guid(bytes.AsSpan(IdLength, IdLength), bigEndian: true);
        return tenant == Guid.Empty || file == Guid.Empty ? null : new PublicMediaReference(TenantId.From(tenant), FileId.From(file));
    }

    private byte[] Sign(ReadOnlySpan<byte> ids)
    {
        var message = new byte[Purpose.Length + ids.Length];
        Purpose.CopyTo(message, 0);
        ids.CopyTo(message.AsSpan(Purpose.Length));
        return HMACSHA256.HashData(_key!, message);
    }
}

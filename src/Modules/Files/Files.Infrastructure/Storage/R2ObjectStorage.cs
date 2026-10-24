using System.Net;
using System.Net.Http.Headers;
using Files.Application.Abstractions;
using Files.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Files.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 par son API compatible S3, en adressage par chemin (<c>{endpoint}/{bucket}/{clé}</c>), signée SigV4.
/// Aucune URL signée, aucun en-tête d'autorisation ni contenu n'est journalisé ; une erreur ne porte que le statut HTTP.
/// </summary>
internal sealed class R2ObjectStorage(HttpClient http, FilesOptions options, IClock clock) : IObjectStorage
{
    private readonly R2Options _r2 = options.R2;
    private readonly SigV4 _signer = new(options.R2.AccessKeyId, options.R2.SecretAccessKey);

    private Uri Endpoint => _r2.ResolveEndpoint();

    public Uri PresignUpload(StorageKey key, ContentType contentType, long sizeInBytes, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(contentType);
        // Type et taille signés : le stockage refuse un envoi d'un autre type ou d'une autre taille que ceux déclarés.
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-type"] = contentType.Value,
            ["content-length"] = sizeInBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return _signer.Presign("PUT", Endpoint, Path(key), headers, new Dictionary<string, string>(), lifetime, clock.UtcNow);
    }

    public Uri PresignDownload(StorageKey key, FileName fileName, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fileName);
        // Toujours en pièce jointe (jamais affiché inline par le navigateur depuis le domaine du stockage).
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response-content-disposition"] = ContentDisposition(fileName.Value),
        };
        return _signer.Presign("GET", Endpoint, Path(key), new Dictionary<string, string>(), query, lifetime, clock.UtcNow);
    }

    public async Task<StoredObject?> InspectAsync(StorageKey key, int headLength, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, key);
        request.Headers.Range = new RangeHeaderValue(0, headLength - 1);
        Sign(request, key, SigV4.UnsignedPayload);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.RequestedRangeNotSatisfiable:
                return new StoredObject(0, []);
        }

        EnsureSuccess(response);
        var head = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var size = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength ?? head.LongLength;
        return new StoredObject(size, head.Length > headLength ? head[..headLength] : head);
    }

    public async Task PutAsync(StorageKey key, ContentType contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var request = Request(HttpMethod.Put, key);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType.Value);
        Sign(request, key, SigV4.Hash(content));
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
    }

    public async Task<byte[]?> GetAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, key);
        Sign(request, key, SigV4.UnsignedPayload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Delete, key);
        Sign(request, key, SigV4.UnsignedPayload);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            EnsureSuccess(response);
        }
    }

    private string Path(StorageKey key) => SigV4.CanonicalPath(_r2.Bucket, key.Value);

    private HttpRequestMessage Request(HttpMethod method, StorageKey key) =>
        new(method, new Uri(Endpoint.GetLeftPart(UriPartial.Authority) + Path(key)));

    private void Sign(HttpRequestMessage request, StorageKey key, string payloadHash) =>
        _signer.SignRequest(request, Path(key), payloadHash, clock.UtcNow);

    // Le statut seulement : ni l'URL (elle contient la clé), ni le corps de la réponse.
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Object storage request failed with status {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    /// <summary>Nom ASCII de repli (caractères hors ASCII remplacés) et nom UTF-8 complet (RFC 6266).</summary>
    private static string ContentDisposition(string fileName)
    {
        var ascii = new string(fileName.Select(c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{SigV4.Encode(fileName)}";
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Files.Infrastructure.Storage;

/// <summary>
/// Signature AWS Signature Version 4, celle de l'API S3 que R2 implémente (région <c>auto</c>, service <c>s3</c>). Deux
/// usages : URL pré-signée (paramètres de requête) et requête signée par l'en-tête <c>Authorization</c>.
/// </summary>
internal sealed class SigV4(string accessKeyId, string secretAccessKey, string region = "auto", string service = "s3")
{
    public const string Algorithm = "AWS4-HMAC-SHA256";
    public const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    /// <summary>URL pré-signée : en-têtes signés (dont <c>host</c>) que le client DOIT envoyer à l'identique.</summary>
    public Uri Presign(
        string method,
        Uri endpoint,
        string canonicalPath,
        IReadOnlyDictionary<string, string> signedHeaders,
        IReadOnlyDictionary<string, string> extraQuery,
        TimeSpan lifetime,
        DateTimeOffset nowUtc)
    {
        var (amzDate, dateStamp) = Dates(nowUtc);
        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var headers = WithHost(signedHeaders, endpoint);
        var signedHeaderNames = string.Join(';', headers.Keys);

        var query = new SortedDictionary<string, string>(extraQuery.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{accessKeyId}/{scope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = ((int)lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = signedHeaderNames,
        };
        var canonicalQuery = CanonicalQuery(query);
        var canonicalRequest = string.Join('\n', method, canonicalPath, canonicalQuery, CanonicalHeaders(headers), signedHeaderNames, UnsignedPayload);
        var signature = Sign(dateStamp, StringToSign(amzDate, scope, canonicalRequest));

        return new Uri($"{endpoint.GetLeftPart(UriPartial.Authority)}{canonicalPath}?{canonicalQuery}&X-Amz-Signature={signature}");
    }

    /// <summary>Signe une requête du serveur (en-têtes <c>host</c>, <c>x-amz-date</c>, <c>x-amz-content-sha256</c>).</summary>
    public void SignRequest(HttpRequestMessage request, string canonicalPath, string payloadHash, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (amzDate, dateStamp) = Dates(nowUtc);
        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var headers = WithHost(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["x-amz-content-sha256"] = payloadHash, ["x-amz-date"] = amzDate },
            request.RequestUri!);
        var signedHeaderNames = string.Join(';', headers.Keys);
        var canonicalRequest = string.Join('\n', request.Method.Method, canonicalPath, string.Empty, CanonicalHeaders(headers), signedHeaderNames, payloadHash);
        var signature = Sign(dateStamp, StringToSign(amzDate, scope, canonicalRequest));

        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation(
            "Authorization", $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={signedHeaderNames}, Signature={signature}");
    }

    /// <summary>Chemin canonique : chaque segment encodé selon la RFC 3986, les « / » conservés.</summary>
    public static string CanonicalPath(string bucket, string key) =>
        "/" + Encode(bucket) + "/" + string.Join('/', key.Split('/').Select(Encode));

    public static string Hash(byte[] payload) => Convert.ToHexStringLower(SHA256.HashData(payload));

    /// <summary>Encodage RFC 3986 : seuls A-Z, a-z, 0-9, « - », « _ », « . », « ~ » restent tels quels.</summary>
    public static string Encode(string value)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static (string AmzDate, string DateStamp) Dates(DateTimeOffset nowUtc)
    {
        var utc = nowUtc.UtcDateTime;
        return (utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture), utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
    }

    private static SortedDictionary<string, string> WithHost(IReadOnlyDictionary<string, string> headers, Uri endpoint)
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            all[name.ToLowerInvariant()] = value.Trim();
        }

        all["host"] = endpoint.IsDefaultPort ? endpoint.Host : $"{endpoint.Host}:{endpoint.Port}";
        return all;
    }

    private static string CanonicalHeaders(SortedDictionary<string, string> headers) =>
        string.Concat(headers.Select(pair => $"{pair.Key}:{pair.Value}\n"));

    private static string CanonicalQuery(SortedDictionary<string, string> query) =>
        string.Join('&', query.Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}"));

    private static string StringToSign(string amzDate, string scope, string canonicalRequest) =>
        string.Join('\n', Algorithm, amzDate, scope, Hash(Encoding.UTF8.GetBytes(canonicalRequest)));

    private string Sign(string dateStamp, string stringToSign)
    {
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), dateStamp);
        key = Hmac(key, region);
        key = Hmac(key, service);
        key = Hmac(key, "aws4_request");
        return Convert.ToHexStringLower(Hmac(key, stringToSign));
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}

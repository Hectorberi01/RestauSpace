using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Integrations.Application.Webhooks;

/// <summary>
/// Signature des webhooks selon la convention « Standard Webhooks » (bibliothèques disponibles côté destinataire) :
/// en-têtes <c>webhook-id</c>, <c>webhook-timestamp</c> (secondes Unix) et <c>webhook-signature</c> =
/// <c>v1,</c> + base64(HMAC-SHA256(clé, « id.timestamp.corps »)). Le secret s'écrit <c>whsec_</c> + base64(32 octets) ;
/// la clé HMAC est ces 32 octets. Le destinataire refuse un horodatage trop ancien (rejeu) et compare en temps constant.
/// </summary>
public static class WebhookSignature
{
    public const string SecretPrefix = "whsec_";
    public const string IdHeader = "webhook-id";
    public const string TimestampHeader = "webhook-timestamp";
    public const string SignatureHeader = "webhook-signature";

    /// <summary>Un nouveau secret : 32 octets aléatoires.</summary>
    public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static IReadOnlyDictionary<string, string> Headers(string secret, string messageId, DateTimeOffset timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Headers(new[] { secret }, messageId, timestamp, body);
    }

    /// <summary>
    /// Une signature par secret, séparées par une espace (« v1,a v1,b ») : pendant la période de grâce d'une rotation,
    /// le destinataire accepte l'envoi qu'il connaisse l'ancien ou le nouveau secret.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Headers(IReadOnlyList<string> secrets, string messageId, DateTimeOffset timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfZero(secrets.Count, nameof(secrets));
        var seconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var content = Encoding.UTF8.GetBytes($"{messageId}.{seconds}.{body}");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdHeader] = messageId,
            [TimestampHeader] = seconds,
            [SignatureHeader] = string.Join(' ', secrets.Select(secret => "v1," + Sign(secret, content))),
        };
    }

    private static string Sign(string secret, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var key = Convert.FromBase64String(secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret);
        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(key, content));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

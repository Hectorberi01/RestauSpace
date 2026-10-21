using System.Net;
using System.Net.Sockets;

namespace Integrations.Domain;

/// <summary>
/// Destination HTTPS absolue sur un nom d'hôte ou une IP publics, sans identifiants intégrés. Les contrôles sont
/// purement lexicaux (ce que dit l'URL) : ce vers quoi le nom SE RÉSOUT doit être revérifié par l'émetteur à la connexion.
/// </summary>
public sealed class WebhookUrl : ValueObject
{
    public const int MaxLength = 2048;

    private WebhookUrl(string value) => Value = value;

    public string Value { get; }

    public string Host => new Uri(Value).Host;

    /// <summary>
    /// Forme affichable et publiable (réponses, événements, journaux) : schéma, hôte et port seulement. Le chemin et la
    /// requête, qui portent souvent un jeton du destinataire, sont remplacés par <c>/***</c>.
    /// </summary>
    public string Redacted
    {
        get
        {
            var uri = new Uri(Value);
            var authority = uri.GetLeftPart(UriPartial.Authority);
            return uri.PathAndQuery is "/" or "" ? authority + "/" : authority + "/***";
        }
    }

    public static WebhookUrl Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), WebhookEndpointErrors.UrlInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(
            trimmed.Length > MaxLength
            || !trimmed.Contains("://", StringComparison.Ordinal)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host),
            WebhookEndpointErrors.UrlInvalid);

        var parsed = new Uri(trimmed, UriKind.Absolute);
        DomainException.ThrowIf(parsed.Scheme != Uri.UriSchemeHttps, WebhookEndpointErrors.UrlNotHttps);
        // L'autorité brute est aussi inspectée : les parseurs divergent sur ce qui suit un '@', et c'est ainsi
        // qu'une URL affichée pour un hôte finit envoyée à un autre.
        DomainException.ThrowIf(
            !string.IsNullOrEmpty(parsed.UserInfo) || RawAuthority(trimmed).Contains('@'),
            WebhookEndpointErrors.UrlHasCredentials);
        DomainException.ThrowIf(parsed.Port == 0, WebhookEndpointErrors.UrlPortInvalid);
        DomainException.ThrowIf(!IsPublicHost(parsed), WebhookEndpointErrors.UrlHostNotPublic);

        return new WebhookUrl(parsed.AbsoluteUri);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    private static string RawAuthority(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal) + 3;
        // Un antislash termine aussi l'autorité : Uri le lit comme '/' pour https.
        var end = url.IndexOfAny(['/', '?', '#', '\\'], start);
        return end < 0 ? url[start..] : url[start..end];
    }

    private static bool IsPublicHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return false;
        }

        // Uri a déjà converti les écritures décimale, octale et hexadécimale d'une adresse IPv4 en notation pointée.
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(uri.DnsSafeHost, out var address) && IsPublicAddress(address);
        }

        var host = uri.IdnHost.TrimEnd('.');
        // Un nom sans point (« intranet », « localhost ») ne se résout que dans le propre réseau de l'émetteur.
        return host.Contains('.')
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            // Un nom d'allure numérique qu'Uri n'a pas lu comme adresse en serait une pour d'autres résolveurs.
            && !IPAddress.TryParse(host, out _);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                169 => bytes[1] != 254,
                172 => bytes[1] is < 16 or > 31,
                192 => bytes[1] != 168,
                _ => true,
            };
        }

        var v6 = address.GetAddressBytes();
        var isUnspecifiedOrLoopback = v6.AsSpan(0, 15).IndexOfAnyExcept((byte)0) < 0 && v6[15] <= 1;
        var isUniqueLocal = (v6[0] & 0xFE) == 0xFC;
        var isLinkLocal = v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80;
        // « ::a.b.c.d » (compatible IPv4) atteint le même hôte IPv4 sur les piles qui le prennent encore en charge.
        var isIPv4Compatible = v6.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0;
        return !isUnspecifiedOrLoopback && !isUniqueLocal && !isLinkLocal && !isIPv4Compatible;
    }
}

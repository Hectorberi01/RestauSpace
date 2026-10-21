using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Outils communs aux intégrations HTTP : lecture des identifiants, appel au prestataire, traduction des statuts HTTP en
/// échecs. Aucune valeur d'identifiant ni adresse de destinataire n'entre jamais dans un message d'erreur.
/// </summary>
internal static class ProviderSupport
{
    public const string AuthFailed = "PROVIDER_AUTH_FAILED";
    public const string NoCredit = "PROVIDER_NO_CREDIT";

    public static HttpClient NewHttpClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Le champ du compte, non vide ; sinon une exception qui ne cite que le NOM du champ.</summary>
    public static string Read(IReadOnlyDictionary<string, string> credentials, string field, string provider)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"The {provider} account has no {field}.");
    }

    /// <summary>Une adresse d'expédition plausible (le prestataire la valide pour de bon).</summary>
    public static string ReadSender(IReadOnlyDictionary<string, string> credentials, string field, string provider)
    {
        var from = Read(credentials, field, provider);
        return from.Contains('@', StringComparison.Ordinal) && !from.Any(char.IsWhiteSpace)
            ? from
            : throw new InvalidOperationException($"The {provider} sender address is invalid.");
    }

    /// <summary>
    /// Une URL de base HTTPS dont l'hôte appartient au prestataire (<paramref name="allowedSuffix"/>) : l'URL vient du
    /// compte saisi, elle ne doit jamais faire appeler un autre serveur.
    /// </summary>
    public static Uri ReadBaseUrl(IReadOnlyDictionary<string, string> credentials, string field, string provider, string allowedSuffix)
    {
        var text = Read(credentials, field, provider);
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text.TrimEnd('/'), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Port == 443
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Host.Equals(allowedSuffix.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(allowedSuffix, StringComparison.OrdinalIgnoreCase))
            ? new Uri(uri.GetLeftPart(UriPartial.Authority))
            : throw new InvalidOperationException($"The {provider} base URL is not allowed.");
    }

    /// <summary>« +22997000000 » -> « 22997000000 » : la forme attendue par les prestataires qui refusent le « + ».</summary>
    public static string Digits(string e164) => e164.TrimStart('+');

    /// <summary>
    /// Envoie la requête et lit la réponse. Réseau coupé ou délai dépassé : un échec temporaire (le corps reste vide).
    /// </summary>
    public static async Task<(HttpStatusCode Status, string Body, NotificationFailure? Transport)> CallAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
        catch (HttpRequestException)
        {
            return (0, string.Empty, NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (0, string.Empty, NotificationFailure.Temporary(NotificationFailure.Timeout));
        }
    }

    /// <summary>Les statuts que tous les prestataires partagent ; null : à traduire selon le corps.</summary>
    public static NotificationFailure? FromStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => NotificationFailure.Temporary(NotificationFailure.RateLimited),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => NotificationFailure.Temporary(AuthFailed),
        HttpStatusCode.PaymentRequired => NotificationFailure.Temporary(NoCredit),
        _ when (int)status >= 500 => NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable),
        _ => null,
    };

    public static JsonNode? Json(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString().Trim('"');
    }

    /// <summary>Un code d'erreur du prestataire réduit à des caractères sûrs, préfixé ; sinon <c>REJECTED</c>.</summary>
    public static string Code(string prefix, string? raw)
    {
        var cleaned = new string((raw ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        return cleaned.Length is > 0 and <= 40 ? prefix + "_" + cleaned.ToUpperInvariant().Replace('-', '_') : NotificationFailure.Rejected;
    }

    public static bool LooksLikeHtml(string body) => SmtpSenderFactory.LooksLikeHtml(body);
}

/// <summary>Authentification des webhooks des prestataires qui ne signent pas : mot de passe en <c>Authorization: Basic</c>.</summary>
public static class WebhookAuth
{
    public static string? Header(IReadOnlyDictionary<string, string> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers.TryGetValue(name, out var value)
            ? value
            : headers.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Le mot de passe de l'en-tête <c>Authorization: Basic</c>, ou null.</summary>
    public static string? BasicPassword(IReadOnlyDictionary<string, string> headers)
    {
        var header = Header(headers, "Authorization");
        if (header is null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var colon = decoded.IndexOf(':', StringComparison.Ordinal);
            return colon < 0 ? null : decoded[(colon + 1)..];
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Le mot de passe Basic reçu est celui du compte (comparaison à temps constant).</summary>
    public static bool BasicMatches(IReadOnlyDictionary<string, string> headers, IReadOnlyDictionary<string, string> credentials, string field)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.TryGetValue(field, out var expected) && !string.IsNullOrEmpty(expected)
            && BasicPassword(headers) is { } given
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Les champs d'un corps <c>application/x-www-form-urlencoded</c>.</summary>
    public static IReadOnlyDictionary<string, string> Form(ReadOnlySpan<byte> body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Encoding.UTF8.GetString(body).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equal = pair.IndexOf('=', StringComparison.Ordinal);
            var key = WebUtility.UrlDecode(equal < 0 ? pair : pair[..equal]);
            result[key] = equal < 0 ? string.Empty : WebUtility.UrlDecode(pair[(equal + 1)..]);
        }

        return result;
    }
}

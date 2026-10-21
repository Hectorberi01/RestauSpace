using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Firebase Cloud Messaging (API HTTP v1), push Android, iOS et web : le compte de service JSON de Firebase (projet,
/// e-mail du compte, clé privée). Le jeton d'accès OAuth 2.0 est obtenu en signant un JWT RS256 avec la clé privée, puis
/// gardé en mémoire jusqu'à son expiration. FCM ne confirme pas la remise : un push accepté reste « envoyé ».
/// </summary>
internal sealed class FirebaseSenderFactory(HttpClient http, IClock clock) : INotificationSenderFactory
{
    public const string Code = "FCM";
    public const string ServiceAccountField = "service_account";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    // Jetons d'accès par compte de service (clé : e-mail + empreinte de la clé), jamais journalisés.
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new(StringComparer.Ordinal);

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Push];

    public IReadOnlyList<string> CredentialFields { get; } = [ServiceAccountField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) =>
        new FirebaseSender(Provider, http, this, ServiceAccount.Parse(ProviderSupport.Read(credentials, ServiceAccountField, "Firebase")));

    /// <summary>Le compte de service lu dans le JSON téléchargé depuis la console Firebase.</summary>
    internal sealed record ServiceAccount(string ProjectId, string ClientEmail, byte[] PrivateKey)
    {
        public static ServiceAccount Parse(string json)
        {
            JsonNode? root = ProviderSupport.Json(json);
            var project = ProviderSupport.Text(root?["project_id"]);
            var email = ProviderSupport.Text(root?["client_email"]);
            var key = ProviderSupport.Text(root?["private_key"]);
            if (string.IsNullOrEmpty(project) || !project.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or ':' or '.')
                || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("The Firebase service account is incomplete.");
            }

            return new ServiceAccount(project, email, PrivateKeyBytes(key));
        }

        /// <summary>
        /// La clé PKCS#8 du PEM, que ses sauts de ligne aient survécu au copier-coller, soient restés échappés (« \n ») ou
        /// aient disparu.
        /// </summary>
        private static byte[] PrivateKeyBytes(string pem)
        {
            const string begin = "-----BEGIN PRIVATE KEY-----";
            const string end = "-----END PRIVATE KEY-----";
            var start = pem.IndexOf(begin, StringComparison.Ordinal);
            var stop = pem.IndexOf(end, StringComparison.Ordinal);
            if (start < 0 || stop < start)
            {
                throw new InvalidOperationException("The Firebase private key is not a PKCS#8 PEM.");
            }

            var base64 = new string(pem[(start + begin.Length)..stop].Replace("\\n", string.Empty, StringComparison.Ordinal)
                .Where(c => !char.IsWhiteSpace(c)).ToArray());
            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("The Firebase private key is not a PKCS#8 PEM.");
            }
        }
    }

    /// <summary>Le JWT signé RS256 échangé contre un jeton d'accès (valable une heure).</summary>
    internal static string Assertion(ServiceAccount account, DateTimeOffset now)
    {
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var claims = new JsonObject
        {
            ["iss"] = account.ClientEmail,
            ["scope"] = Scope,
            ["aud"] = TokenUrl,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
        };
        var payload = Encode(Encoding.UTF8.GetBytes(claims.ToJsonString()));
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(account.PrivateKey, out _);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Encode(signature)}";
    }

    /// <summary>Un jeton d'accès valable encore au moins une minute ; null si Google refuse le compte.</summary>
    internal async Task<string?> AccessTokenAsync(ServiceAccount account, CancellationToken cancellationToken)
    {
        var key = account.ClientEmail + ":" + Convert.ToHexString(SHA256.HashData(account.PrivateKey));
        var now = clock.UtcNow;
        if (_tokens.TryGetValue(key, out var cached) && cached.ExpiresAt > now.AddMinutes(1))
        {
            return cached.Token;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"), new("assertion", Assertion(account, now)),
            ]),
        };
        var (status, body, transport) = await ProviderSupport.CallAsync(http, request, cancellationToken);
        var json = ProviderSupport.Json(body);
        var token = ProviderSupport.Text(json?["access_token"]);
        if (transport is not null || (int)status is < 200 or >= 300 || string.IsNullOrEmpty(token))
        {
            return null;
        }

        var lifetime = long.TryParse(ProviderSupport.Text(json?["expires_in"]), out var seconds) ? seconds : 3600;
        _tokens[key] = (token, now.AddSeconds(lifetime));
        return token;
    }

    internal static JsonObject Payload(NotificationSendRequest request) => new()
    {
        ["message"] = new JsonObject
        {
            ["token"] = request.Recipient.Address,
            ["notification"] = new JsonObject { ["title"] = request.Content.Subject ?? string.Empty, ["body"] = request.Content.Body },
            ["data"] = new JsonObject { ["notification_id"] = request.NotificationId.Value.ToString("N"), ["type"] = request.Type },
        },
    };

    /// <summary>Le statut d'erreur FCM (<c>UNREGISTERED</c>, <c>INVALID_ARGUMENT</c>…), jamais son message.</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        var json = ProviderSupport.Json(body);
        var details = json?["error"]?["details"] as JsonArray;
        var fcmCode = details?.Select(detail => ProviderSupport.Text(detail?["errorCode"])).FirstOrDefault(code => code is not null);
        var code = fcmCode ?? ProviderSupport.Text(json?["error"]?["status"]);
        return code switch
        {
            "UNREGISTERED" or "INVALID_ARGUMENT" or "NOT_FOUND" => NotificationFailure.Permanent(NotificationFailure.InvalidDeviceToken),
            "SENDER_ID_MISMATCH" => NotificationFailure.Permanent("SENDER_ID_MISMATCH"),
            "QUOTA_EXCEEDED" or "RESOURCE_EXHAUSTED" => NotificationFailure.Temporary(NotificationFailure.RateLimited),
            "UNAVAILABLE" or "INTERNAL" => NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable),
            _ => ProviderSupport.FromStatus(status) ?? NotificationFailure.Permanent(ProviderSupport.Code("FCM", code)),
        };
    }

    private sealed class FirebaseSender(NotificationProviderCode provider, HttpClient http, FirebaseSenderFactory factory, ServiceAccount account)
        : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => NotificationChannel.Push;

        public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var token = await factory.AccessTokenAsync(account, cancellationToken);
            if (token is null)
            {
                return NotificationSendResult.Failed(NotificationFailure.Temporary(ProviderSupport.AuthFailed));
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{account.ProjectId}/messages:send")
            {
                Content = new StringContent(Payload(request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var (status, body, transport) = await ProviderSupport.CallAsync(http, message, cancellationToken);
            if (transport is not null)
            {
                return NotificationSendResult.Failed(transport);
            }

            if ((int)status is >= 200 and < 300)
            {
                var name = ProviderSupport.Text(ProviderSupport.Json(body)?["name"]);
                var id = name?.Split('/').LastOrDefault();
                return NotificationSendResult.Accepted(id is { Length: > 0 and <= ProviderMessageId.MaxLength } ? ProviderMessageId.Create(id) : null);
            }

            return NotificationSendResult.Failed(Translate(status, body));
        }
    }
}

/// <summary>
/// OneSignal, push marketing et segmentation : identifiant de l'application et clé REST API. L'adresse du destinataire
/// est l'identifiant d'abonnement OneSignal de l'appareil (SDK OneSignal dans l'application). Pas d'accusé de remise relié.
/// </summary>
internal sealed class OneSignalSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "ONESIGNAL";
    public const string AppIdField = "app_id";
    public const string ApiKeyField = "rest_api_key";
    public const string SendUrl = "https://api.onesignal.com/notifications";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Push];

    public IReadOnlyList<string> CredentialFields { get; } = [AppIdField, ApiKeyField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        var appId = ProviderSupport.Read(credentials, AppIdField, "OneSignal");
        if (!Guid.TryParse(appId, out _))
        {
            throw new InvalidOperationException("The OneSignal app id is invalid.");
        }

        return new OneSignalSender(Provider, http, appId, ProviderSupport.Read(credentials, ApiKeyField, "OneSignal"));
    }

    internal static JsonObject Payload(string appId, NotificationSendRequest request)
    {
        var title = request.Content.Subject ?? string.Empty;
        return new JsonObject
        {
            ["app_id"] = appId,
            ["include_subscription_ids"] = new JsonArray(request.Recipient.Address),
            ["headings"] = new JsonObject { ["en"] = title, ["fr"] = title },
            ["contents"] = new JsonObject { ["en"] = request.Content.Body, ["fr"] = request.Content.Body },
            ["data"] = new JsonObject { ["notification_id"] = request.NotificationId.Value.ToString("N"), ["type"] = request.Type },
            ["idempotency_key"] = request.DeliveryId.Value.ToString(),
        };
    }

    /// <summary>
    /// OneSignal répond 200 même quand aucun appareil n'est joignable : <c>errors.invalid_player_ids</c> ou l'absence
    /// d'abonné valent un jeton invalide.
    /// </summary>
    internal static NotificationSendResult ReadResponse(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return NotificationSendResult.Failed(common);
        }

        var json = ProviderSupport.Json(body);
        var id = ProviderSupport.Text(json?["id"]);
        var errors = json?["errors"];
        var unreachable = errors is JsonObject objectErrors && (objectErrors.ContainsKey("invalid_player_ids") || objectErrors.ContainsKey("invalid_aliases"))
            || errors is JsonArray list && list.Count > 0;
        if ((int)status is >= 200 and < 300 && !string.IsNullOrEmpty(id) && !unreachable)
        {
            return NotificationSendResult.Accepted(id.Length <= ProviderMessageId.MaxLength ? ProviderMessageId.Create(id) : null);
        }

        return NotificationSendResult.Failed(unreachable || (int)status is >= 200 and < 300
            ? NotificationFailure.Permanent(NotificationFailure.InvalidDeviceToken)
            : NotificationFailure.Permanent(NotificationFailure.Rejected));
    }

    private sealed class OneSignalSender(NotificationProviderCode provider, HttpClient http, string appId, string apiKey)
        : HttpNotificationSender(provider, NotificationChannel.Push, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(Payload(appId, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.TryAddWithoutValidation("Authorization", "Key " + apiKey);
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) => ReadResponse(status, body);
    }
}

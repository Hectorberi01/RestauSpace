using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Resend, email transactionnel : clé API, expéditeur (domaine vérifié chez Resend) et secret des webhooks
/// (<c>whsec_…</c>, signature Svix vérifiée par <see cref="ResendWebhookReader"/>).
/// </summary>
internal sealed class ResendSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "RESEND";
    public const string ApiKeyField = "api_key";
    public const string FromEmailField = "from_email";
    public const string FromNameField = "from_name";
    public const string WebhookSecretField = "webhook_secret";
    public const string SendUrl = "https://api.resend.com/emails";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Email];

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKeyField, FromEmailField, FromNameField, WebhookSecretField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) => new ResendSender(
        Provider, http, ProviderSupport.Read(credentials, ApiKeyField, "Resend"),
        ProviderSupport.ReadSender(credentials, FromEmailField, "Resend"), ProviderSupport.Read(credentials, FromNameField, "Resend"));

    internal static JsonObject Payload(string fromEmail, string fromName, NotificationSendRequest request)
    {
        var payload = new JsonObject
        {
            ["from"] = $"{fromName.Replace("\"", string.Empty, StringComparison.Ordinal)} <{fromEmail}>",
            ["to"] = new JsonArray(request.Recipient.Address),
            ["subject"] = request.Content.Subject ?? string.Empty,
            ["tags"] = new JsonArray(new JsonObject { ["name"] = "delivery", ["value"] = request.DeliveryId.Value.ToString("N") }),
        };
        payload[ProviderSupport.LooksLikeHtml(request.Content.Body) ? "html" : "text"] = request.Content.Body;
        return payload;
    }

    /// <summary>Le nom d'erreur de Resend (<c>validation_error</c>…), jamais son message.</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        var name = ProviderSupport.Text(ProviderSupport.Json(body)?["name"]);
        return name is "validation_error" or "invalid_to_address"
            ? NotificationFailure.Permanent(NotificationFailure.InvalidEmail)
            : NotificationFailure.Permanent(ProviderSupport.Code("RESEND", name));
    }

    private sealed class ResendSender(NotificationProviderCode provider, HttpClient http, string apiKey, string fromEmail, string fromName)
        : HttpNotificationSender(provider, NotificationChannel.Email, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(Payload(fromEmail, fromName, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Add("Idempotency-Key", request.DeliveryId.Value.ToString("N"));
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) =>
            (int)status is >= 200 and < 300
                ? Accepted(ProviderSupport.Text(ProviderSupport.Json(body)?["id"]))
                : NotificationSendResult.Failed(Translate(status, body));
    }
}

/// <summary>
/// Webhooks de Resend, signés au format Svix : <c>svix-signature</c> = « v1,&lt;HMAC-SHA256 base64&gt; » de
/// « id.timestamp.corps », clé = le secret <c>whsec_…</c> décodé. Un horodatage à plus de 5 minutes est refusé (rejeu).
/// <c>email.delivered</c> = remis ; <c>email.bounced</c> = échec définitif ; le reste est gardé sans effet.
/// </summary>
public sealed class ResendWebhookReader(IClock clock) : INotificationWebhookReader
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(ResendSenderFactory.Code);

    public static string Sign(string secret, string id, string timestamp, byte[] body)
    {
        var key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret);
        var signed = Encoding.UTF8.GetBytes($"{id}.{timestamp}.").Concat(body).ToArray();
        return Convert.ToBase64String(HMACSHA256.HashData(key, signed));
    }

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var id = WebhookAuth.Header(headers, "svix-id");
        var timestamp = WebhookAuth.Header(headers, "svix-timestamp");
        var signatures = WebhookAuth.Header(headers, "svix-signature");
        if (!credentials.TryGetValue(ResendSenderFactory.WebhookSecretField, out var secret) || string.IsNullOrEmpty(secret)
            || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(signatures)
            || !long.TryParse(timestamp, out var seconds)
            || (clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Tolerance)
        {
            return null;
        }

        string expected;
        try
        {
            expected = Sign(secret, id, timestamp!, body.ToArray());
        }
        catch (FormatException)
        {
            return null;
        }

        var valid = signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.StartsWith("v1,", StringComparison.Ordinal) ? part[3..] : null)
            .Any(given => given is not null
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(expected)));
        if (!valid)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            var type = WebhookJson.Text(root, "type");
            var messageId = root.TryGetProperty("data", out var data) ? WebhookJson.Text(data, "email_id") : null;
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(messageId))
            {
                return [];
            }

            var (kind, failure, permanent) = type switch
            {
                "email.delivered" => (WebhookEventKind.Delivered, (string?)null, false),
                "email.bounced" => (WebhookEventKind.Failed, NotificationFailure.Bounced, true),
                _ => (WebhookEventKind.Ignored, (string?)null, false),
            };
            return [new ProviderWebhookEvent(id, kind, messageId, failure, permanent)];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

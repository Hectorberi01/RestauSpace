using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Brevo (ex-Sendinblue) par l'API v3, en deux intégrations car un prestataire du catalogue n'a qu'un canal :
/// <c>BREVO</c> pour l'email (clé API, expéditeur validé chez Brevo) et <c>BREVO_SMS</c> pour le SMS (clé API,
/// expéditeur de 11 caractères alphanumériques au plus). La remise est confirmée par le webhook transactionnel de Brevo
/// (<see cref="BrevoWebhookReader"/>).
/// </summary>
internal sealed class BrevoSenderFactory(HttpClient http, NotificationChannel channel = NotificationChannel.Email) : INotificationSenderFactory
{
    public const string Code = "BREVO";
    public const string SmsCode = "BREVO_SMS";
    public const string ApiKeyField = "api_key";
    public const string FromEmailField = "from_email";
    public const string FromNameField = "from_name";
    public const string SmsSenderField = "sms_sender";
    public const string WebhookPasswordField = "webhook_password";
    public const string EmailUrl = "https://api.brevo.com/v3/smtp/email";
    public const string SmsUrl = "https://api.brevo.com/v3/transactionalSMS/sms";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(channel == NotificationChannel.Sms ? SmsCode : Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [channel];

    public IReadOnlyList<string> CredentialFields { get; } = channel == NotificationChannel.Sms
        ? [ApiKeyField, SmsSenderField, WebhookPasswordField]
        : [ApiKeyField, FromEmailField, FromNameField, WebhookPasswordField];

    public INotificationSender Create(NotificationChannel requested, IReadOnlyDictionary<string, string> credentials)
    {
        var apiKey = ProviderSupport.Read(credentials, ApiKeyField, "Brevo");
        return channel == NotificationChannel.Sms
            ? new BrevoSmsSender(Provider, http, apiKey, ProviderSupport.Read(credentials, SmsSenderField, "Brevo"))
            : new BrevoEmailSender(
                Provider, http, apiKey, ProviderSupport.ReadSender(credentials, FromEmailField, "Brevo"), ProviderSupport.Read(credentials, FromNameField, "Brevo"));
    }

    internal static JsonObject EmailPayload(string fromEmail, string fromName, NotificationSendRequest request)
    {
        var payload = new JsonObject
        {
            ["sender"] = new JsonObject { ["email"] = fromEmail, ["name"] = fromName },
            ["to"] = new JsonArray(new JsonObject { ["email"] = request.Recipient.Address }),
            ["subject"] = request.Content.Subject ?? string.Empty,
            ["headers"] = new JsonObject { ["X-Mailin-custom"] = request.DeliveryId.Value.ToString("N") },
        };
        payload[ProviderSupport.LooksLikeHtml(request.Content.Body) ? "htmlContent" : "textContent"] = request.Content.Body;
        return payload;
    }

    internal static JsonObject SmsPayload(string sender, NotificationSendRequest request) => new()
    {
        ["sender"] = sender,
        ["recipient"] = ProviderSupport.Digits(request.Recipient.Address),
        ["content"] = request.Content.Body,
        ["type"] = "transactional",
        ["tag"] = request.Type,
    };

    /// <summary>Le code d'erreur de Brevo (<c>invalid_parameter</c>, <c>not_enough_credits</c>…), jamais son message.</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body, NotificationChannel channel)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        var code = ProviderSupport.Text(ProviderSupport.Json(body)?["code"]);
        return code switch
        {
            "not_enough_credits" => NotificationFailure.Temporary(ProviderSupport.NoCredit),
            "invalid_parameter" or "missing_parameter" => NotificationFailure.Permanent(
                channel == NotificationChannel.Sms ? NotificationFailure.InvalidPhoneNumber : NotificationFailure.InvalidEmail),
            _ => NotificationFailure.Permanent(ProviderSupport.Code("BREVO", code)),
        };
    }

    private sealed class BrevoEmailSender(NotificationProviderCode provider, HttpClient http, string apiKey, string fromEmail, string fromName)
        : HttpNotificationSender(provider, NotificationChannel.Email, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, EmailUrl)
            {
                Content = new StringContent(EmailPayload(fromEmail, fromName, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("api-key", apiKey);
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) =>
            (int)status is >= 200 and < 300
                ? Accepted(ProviderSupport.Text(ProviderSupport.Json(body)?["messageId"]))
                : NotificationSendResult.Failed(Translate(status, body, NotificationChannel.Email));
    }

    private sealed class BrevoSmsSender(NotificationProviderCode provider, HttpClient http, string apiKey, string sender)
        : HttpNotificationSender(provider, NotificationChannel.Sms, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, SmsUrl)
            {
                Content = new StringContent(SmsPayload(sender, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("api-key", apiKey);
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) =>
            (int)status is >= 200 and < 300
                ? Accepted(ProviderSupport.Text(ProviderSupport.Json(body)?["messageId"]))
                : NotificationSendResult.Failed(Translate(status, body, NotificationChannel.Sms));
    }
}

/// <summary>
/// Webhooks transactionnels de Brevo (email et SMS). Brevo ne signe pas : l'URL déclarée chez Brevo porte le mot de
/// passe du compte en authentification Basic. Email : <c>delivered</c> = remis ; <c>hard_bounce</c>, <c>invalid_email</c>,
/// <c>blocked</c>, <c>error</c> = échec définitif ; <c>soft_bounce</c> = échec temporaire. SMS : <c>delivered</c> = remis ;
/// <c>hardBounce</c>, <c>rejected</c> = échec définitif ; <c>softBounce</c> = échec temporaire.
/// </summary>
public sealed class BrevoWebhookReader(string code = BrevoSenderFactory.Code) : INotificationWebhookReader
{
    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(code);

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        if (!WebhookAuth.BasicMatches(headers, credentials, BrevoSenderFactory.WebhookPasswordField))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
            var events = new List<ProviderWebhookEvent>();
            foreach (var item in items.Where(item => item.ValueKind == JsonValueKind.Object))
            {
                var kindText = WebhookJson.Text(item, "event") ?? WebhookJson.Text(item, "msg_status");
                var messageId = WebhookJson.Text(item, "message-id") ?? WebhookJson.Text(item, "messageId");
                if (string.IsNullOrEmpty(kindText) || string.IsNullOrEmpty(messageId))
                {
                    continue;
                }

                var (kind, failure, permanent) = kindText switch
                {
                    "delivered" => (WebhookEventKind.Delivered, (string?)null, false),
                    "hard_bounce" or "hardBounce" or "invalid_email" => (WebhookEventKind.Failed, NotificationFailure.Bounced, true),
                    "blocked" or "error" or "rejected" => (WebhookEventKind.Failed, NotificationFailure.Rejected, true),
                    "soft_bounce" or "softBounce" => (WebhookEventKind.Failed, "SOFT_BOUNCE", false),
                    _ => (WebhookEventKind.Ignored, (string?)null, false),
                };
                var at = WebhookJson.Text(item, "ts_event") ?? WebhookJson.Text(item, "ts") ?? WebhookJson.Text(item, "date") ?? string.Empty;
                events.Add(new ProviderWebhookEvent($"{messageId}:{kindText}:{at}", kind, messageId, failure, permanent));
            }

            return events;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Lecture tolérante des champs d'un événement JSON (texte ou nombre).</summary>
internal static class WebhookJson
{
    public static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;
}

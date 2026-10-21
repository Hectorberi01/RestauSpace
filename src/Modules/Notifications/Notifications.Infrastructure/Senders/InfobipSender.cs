using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Infobip, SMS Afrique et international : URL de base du compte (<c>xxxxx.api.infobip.com</c>), clé API, expéditeur et
/// mot de passe des rapports de remise. L'identifiant du message est celui de la tentative, choisi par nous.
/// </summary>
internal sealed class InfobipSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "INFOBIP";
    public const string BaseUrlField = "base_url";
    public const string ApiKeyField = "api_key";
    public const string SenderField = "sender";
    public const string WebhookPasswordField = "webhook_password";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Sms];

    public IReadOnlyList<string> CredentialFields { get; } = [BaseUrlField, ApiKeyField, SenderField, WebhookPasswordField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) => new InfobipSender(
        Provider, http, ProviderSupport.ReadBaseUrl(credentials, BaseUrlField, "Infobip", ".api.infobip.com"),
        ProviderSupport.Read(credentials, ApiKeyField, "Infobip"), ProviderSupport.Read(credentials, SenderField, "Infobip"));

    internal static JsonObject Payload(string sender, NotificationSendRequest request) => new()
    {
        ["messages"] = new JsonArray(new JsonObject
        {
            ["destinations"] = new JsonArray(new JsonObject
            {
                ["to"] = ProviderSupport.Digits(request.Recipient.Address),
                ["messageId"] = request.DeliveryId.Value.ToString("N"),
            }),
            ["from"] = sender,
            ["text"] = request.Content.Body,
        }),
    };

    /// <summary>Le nom d'erreur d'Infobip (<c>BAD_REQUEST</c>…), jamais son texte.</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        var error = ProviderSupport.Json(body)?["requestError"]?["serviceException"]?["messageId"];
        return NotificationFailure.Permanent(ProviderSupport.Code("INFOBIP", ProviderSupport.Text(error)));
    }

    private sealed class InfobipSender(NotificationProviderCode provider, HttpClient http, Uri baseUrl, string apiKey, string sender)
        : HttpNotificationSender(provider, NotificationChannel.Sms, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "/sms/2/text/advanced"))
            {
                Content = new StringContent(Payload(sender, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("App", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body)
        {
            if ((int)status is < 200 or >= 300)
            {
                return NotificationSendResult.Failed(Translate(status, body));
            }

            var first = (ProviderSupport.Json(body)?["messages"] as JsonArray)?.FirstOrDefault();
            var group = ProviderSupport.Text(first?["status"]?["groupName"]);
            return group == "REJECTED"
                ? NotificationSendResult.Failed(NotificationFailure.Permanent(ProviderSupport.Code("INFOBIP", ProviderSupport.Text(first?["status"]?["name"]))))
                : Accepted(ProviderSupport.Text(first?["messageId"]));
        }
    }
}

/// <summary>
/// Rapports de remise d'Infobip (<c>{"results":[…]}</c>), authentifiés par le mot de passe du compte en Basic dans
/// l'URL de notification. Groupe <c>DELIVERED</c> = remis ; <c>UNDELIVERABLE</c>, <c>EXPIRED</c>, <c>REJECTED</c> = échec.
/// </summary>
public sealed class InfobipWebhookReader : INotificationWebhookReader
{
    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(InfobipSenderFactory.Code);

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        if (!WebhookAuth.BasicMatches(headers, credentials, InfobipSenderFactory.WebhookPasswordField))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var events = new List<ProviderWebhookEvent>();
            foreach (var item in results.EnumerateArray())
            {
                var messageId = WebhookJson.Text(item, "messageId");
                var group = item.TryGetProperty("status", out var status) ? WebhookJson.Text(status, "groupName") : null;
                if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(group))
                {
                    continue;
                }

                var errorGroup = item.TryGetProperty("error", out var error) ? WebhookJson.Text(error, "groupName") : null;
                var (kind, failure, permanent) = group switch
                {
                    "DELIVERED" => (WebhookEventKind.Delivered, (string?)null, false),
                    "UNDELIVERABLE" or "REJECTED" => (WebhookEventKind.Failed,
                        errorGroup == "HANDSET_ERRORS" ? NotificationFailure.InvalidPhoneNumber : ProviderSupport.Code("INFOBIP", group), true),
                    "EXPIRED" => (WebhookEventKind.Failed, "EXPIRED", false),
                    _ => (WebhookEventKind.Ignored, (string?)null, false),
                };
                events.Add(new ProviderWebhookEvent($"{messageId}:{group}", kind, messageId, failure, permanent));
            }

            return events;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

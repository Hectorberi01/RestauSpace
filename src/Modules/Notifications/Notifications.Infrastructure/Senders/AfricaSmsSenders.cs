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
/// Termii, SMS et codes en Afrique : URL de base du compte (<c>….termii.com</c>), clé API, identifiant d'expéditeur
/// (validé chez Termii) et clé secrète des webhooks (signature HMAC-SHA512 vérifiée par <see cref="TermiiWebhookReader"/>).
/// </summary>
internal sealed class TermiiSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "TERMII";
    public const string BaseUrlField = "base_url";
    public const string ApiKeyField = "api_key";
    public const string SenderIdField = "sender_id";
    public const string WebhookSecretField = "webhook_secret";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Sms];

    public IReadOnlyList<string> CredentialFields { get; } = [BaseUrlField, ApiKeyField, SenderIdField, WebhookSecretField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) => new TermiiSender(
        Provider, http, ProviderSupport.ReadBaseUrl(credentials, BaseUrlField, "Termii", ".termii.com"),
        ProviderSupport.Read(credentials, ApiKeyField, "Termii"), ProviderSupport.Read(credentials, SenderIdField, "Termii"));

    // La clé voyage dans le corps (exigence de Termii) : le corps n'est jamais journalisé.
    internal static JsonObject Payload(string apiKey, string senderId, NotificationSendRequest request) => new()
    {
        ["to"] = ProviderSupport.Digits(request.Recipient.Address),
        ["from"] = senderId,
        ["sms"] = request.Content.Body,
        ["type"] = "plain",
        ["channel"] = "generic",
        ["api_key"] = apiKey,
    };

    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        // Termii ne donne qu'un message libre : seul « insufficient balance » est reconnu, le reste est un refus.
        var text = ProviderSupport.Text(ProviderSupport.Json(body)?["message"]) ?? string.Empty;
        return text.Contains("balance", StringComparison.OrdinalIgnoreCase)
            ? NotificationFailure.Temporary(ProviderSupport.NoCredit)
            : NotificationFailure.Permanent(NotificationFailure.Rejected);
    }

    private sealed class TermiiSender(NotificationProviderCode provider, HttpClient http, Uri baseUrl, string apiKey, string senderId)
        : HttpNotificationSender(provider, NotificationChannel.Sms, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "/api/sms/send"))
            {
                Content = new StringContent(Payload(apiKey, senderId, request).ToJsonString(), Encoding.UTF8, "application/json"),
            });

        protected override NotificationSendResult Read(HttpStatusCode status, string body)
        {
            var json = ProviderSupport.Json(body);
            var messageId = ProviderSupport.Text(json?["message_id"]);
            return (int)status is >= 200 and < 300 && !string.IsNullOrEmpty(messageId)
                ? Accepted(messageId)
                : NotificationSendResult.Failed(Translate((int)status is >= 200 and < 300 ? HttpStatusCode.BadRequest : status, body));
        }
    }
}

/// <summary>
/// Webhooks de Termii : en-tête <c>X-Termii-Signature</c> = HMAC-SHA512 hexadécimal du corps avec la clé secrète du
/// compte. Statut <c>Delivered</c> = remis ; <c>Message Failed</c>, <c>Rejected</c>, <c>Expired</c> = échec ; un numéro sous
/// DND est un échec définitif.
/// </summary>
public sealed class TermiiWebhookReader : INotificationWebhookReader
{
    public const string SignatureHeader = "X-Termii-Signature";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(TermiiSenderFactory.Code);

    public static string Sign(string secret, byte[] body) =>
        Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var signature = WebhookAuth.Header(headers, SignatureHeader);
        if (!credentials.TryGetValue(TermiiSenderFactory.WebhookSecretField, out var secret) || string.IsNullOrEmpty(secret)
            || string.IsNullOrEmpty(signature)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Sign(secret, body.ToArray())), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant())))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var item = document.RootElement;
            var messageId = WebhookJson.Text(item, "message_id");
            var status = WebhookJson.Text(item, "status");
            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(status))
            {
                return [];
            }

            var (kind, failure, permanent) = status.ToLowerInvariant() switch
            {
                "delivered" => (WebhookEventKind.Delivered, (string?)null, false),
                var text when text.Contains("dnd", StringComparison.Ordinal) => (WebhookEventKind.Failed, NotificationFailure.Unsubscribed, true),
                "message failed" or "rejected" => (WebhookEventKind.Failed, NotificationFailure.Rejected, true),
                "expired" => (WebhookEventKind.Failed, "EXPIRED", false),
                _ => (WebhookEventKind.Ignored, (string?)null, false),
            };
            return [new ProviderWebhookEvent($"{messageId}:{status}:{WebhookJson.Text(item, "id")}", kind, messageId, failure, permanent)];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Africa's Talking, SMS Afrique : nom d'utilisateur (<c>sandbox</c> = environnement de test), clé API, expéditeur
/// (alphanumérique ou code court enregistré) et mot de passe des rapports de remise.
/// </summary>
internal sealed class AfricasTalkingSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "AFRICASTALKING";
    public const string UsernameField = "username";
    public const string ApiKeyField = "api_key";
    public const string SenderIdField = "sender_id";
    public const string WebhookPasswordField = "webhook_password";
    public const string LiveUrl = "https://api.africastalking.com/version1/messaging";
    public const string SandboxUrl = "https://api.sandbox.africastalking.com/version1/messaging";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Sms];

    public IReadOnlyList<string> CredentialFields { get; } = [UsernameField, ApiKeyField, SenderIdField, WebhookPasswordField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) => new AfricasTalkingSender(
        Provider, http, ProviderSupport.Read(credentials, UsernameField, "Africa's Talking"), ProviderSupport.Read(credentials, ApiKeyField, "Africa's Talking"),
        ProviderSupport.Read(credentials, SenderIdField, "Africa's Talking"));

    /// <summary>Le statut du destinataire (101 envoyé, 102 en file…), jamais le texte.</summary>
    internal static NotificationSendResult ReadRecipient(JsonNode? recipient)
    {
        var code = ProviderSupport.Text(recipient?["statusCode"]);
        return code switch
        {
            "100" or "101" or "102" => NotificationSendResult.Accepted(
                ProviderSupport.Text(recipient?["messageId"]) is { Length: > 0 } id && !id.Equals("None", StringComparison.Ordinal) ? ProviderMessageId.Create(id) : null),
            "403" => NotificationSendResult.Failed(NotificationFailure.Permanent(NotificationFailure.InvalidPhoneNumber)),
            "405" => NotificationSendResult.Failed(NotificationFailure.Temporary(ProviderSupport.NoCredit)),
            "406" => NotificationSendResult.Failed(NotificationFailure.Permanent(NotificationFailure.Unsubscribed)),
            "500" or "501" or "502" => NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable)),
            _ => NotificationSendResult.Failed(NotificationFailure.Permanent(ProviderSupport.Code("AT", code))),
        };
    }

    private sealed class AfricasTalkingSender(NotificationProviderCode provider, HttpClient http, string username, string apiKey, string senderId)
        : HttpNotificationSender(provider, NotificationChannel.Sms, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var url = username.Equals("sandbox", StringComparison.OrdinalIgnoreCase) ? SandboxUrl : LiveUrl;
            var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("username", username), new("to", request.Recipient.Address), new("message", request.Content.Body), new("from", senderId),
                ]),
            };
            message.Headers.Add("apiKey", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body)
        {
            if (ProviderSupport.FromStatus(status) is { } common)
            {
                return NotificationSendResult.Failed(common);
            }

            var recipient = (ProviderSupport.Json(body)?["SMSMessageData"]?["Recipients"] as JsonArray)?.FirstOrDefault();
            return recipient is null
                ? NotificationSendResult.Failed(NotificationFailure.Permanent(NotificationFailure.Rejected))
                : ReadRecipient(recipient);
        }
    }
}

/// <summary>
/// Rapports de remise d'Africa's Talking (corps <c>application/x-www-form-urlencoded</c> : id, status, failureReason),
/// authentifiés par le mot de passe du compte en Basic dans l'URL de rappel. <c>Success</c> = remis ;
/// <c>Failed</c>/<c>Rejected</c> = échec.
/// </summary>
public sealed class AfricasTalkingWebhookReader : INotificationWebhookReader
{
    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(AfricasTalkingSenderFactory.Code);

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        if (!WebhookAuth.BasicMatches(headers, credentials, AfricasTalkingSenderFactory.WebhookPasswordField))
        {
            return null;
        }

        var form = WebhookAuth.Form(body);
        if (!form.TryGetValue("id", out var id) || !form.TryGetValue("status", out var status) || id.Length == 0)
        {
            return [];
        }

        form.TryGetValue("failureReason", out var reason);
        var (kind, failure, permanent) = status switch
        {
            "Success" => (WebhookEventKind.Delivered, (string?)null, false),
            "Failed" or "Rejected" => reason switch
            {
                "UserInBlacklist" or "UserOptedOut" => (WebhookEventKind.Failed, NotificationFailure.Unsubscribed, true),
                "InvalidPhoneNumber" or "AbsentSubscriber" => (WebhookEventKind.Failed, NotificationFailure.InvalidPhoneNumber, true),
                _ => (WebhookEventKind.Failed, ProviderSupport.Code("AT", reason is { Length: > 0 } ? reason : status), false),
            },
            _ => (WebhookEventKind.Ignored, (string?)null, false),
        };
        return [new ProviderWebhookEvent($"{id}:{status}", kind, id, failure, permanent)];
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Twilio en deux intégrations (un prestataire du catalogue n'a qu'un canal) : <c>TWILIO</c> pour le SMS et
/// <c>TWILIO_WHATSAPP</c> pour WhatsApp. SID du compte, jeton d'authentification, expéditeur (numéro E.164, ou SID d'un Messaging
/// Service <c>MG…</c>) et mot de passe des webhooks de statut. Sur WhatsApp, un message libre n'est accepté que dans la
/// fenêtre de 24 heures ouverte par le client ; au-delà, Twilio exige un modèle approuvé.
/// </summary>
internal sealed class TwilioSenderFactory(HttpClient http, NotificationChannel channel = NotificationChannel.Sms) : INotificationSenderFactory
{
    public const string Code = "TWILIO";
    public const string WhatsAppCode = "TWILIO_WHATSAPP";
    public const string AccountSidField = "account_sid";
    public const string AuthTokenField = "auth_token";
    public const string FromField = "from";
    public const string WebhookPasswordField = "webhook_password";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(channel == NotificationChannel.WhatsApp ? WhatsAppCode : Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [channel];

    public IReadOnlyList<string> CredentialFields { get; } = [AccountSidField, AuthTokenField, FromField, WebhookPasswordField];

    public INotificationSender Create(NotificationChannel requested, IReadOnlyDictionary<string, string> credentials)
    {
        var sid = ProviderSupport.Read(credentials, AccountSidField, "Twilio");
        if (!sid.StartsWith("AC", StringComparison.Ordinal) || !sid.All(char.IsAsciiLetterOrDigit))
        {
            throw new InvalidOperationException("The Twilio account SID is invalid.");
        }

        return new TwilioSender(Provider, channel, http, sid, ProviderSupport.Read(credentials, AuthTokenField, "Twilio"), ProviderSupport.Read(credentials, FromField, "Twilio"));
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> Form(NotificationChannel channel, string from, NotificationSendRequest request)
    {
        var prefix = channel == NotificationChannel.WhatsApp ? "whatsapp:" : string.Empty;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", prefix + request.Recipient.Address),
            new("Body", request.Content.Body),
        };
        fields.Add(from.StartsWith("MG", StringComparison.Ordinal)
            ? new KeyValuePair<string, string>("MessagingServiceSid", from)
            : new KeyValuePair<string, string>("From", prefix + from.Replace("whatsapp:", string.Empty, StringComparison.Ordinal)));
        return fields;
    }

    /// <summary>Le code numérique d'erreur de Twilio, jamais son message (il cite le numéro).</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        var code = ProviderSupport.Text(ProviderSupport.Json(body)?["code"]);
        return code switch
        {
            "21211" or "21614" or "21217" => NotificationFailure.Permanent(NotificationFailure.InvalidPhoneNumber),
            "21610" => NotificationFailure.Permanent(NotificationFailure.Unsubscribed),
            "21408" or "21612" => NotificationFailure.Permanent("REGION_NOT_ALLOWED"),
            "20429" or "14107" => NotificationFailure.Temporary(NotificationFailure.RateLimited),
            "20003" => NotificationFailure.Temporary(ProviderSupport.AuthFailed),
            _ => NotificationFailure.Permanent(ProviderSupport.Code("TWILIO", code)),
        };
    }

    private sealed class TwilioSender(NotificationProviderCode provider, NotificationChannel channel, HttpClient http, string sid, string token, string from)
        : HttpNotificationSender(provider, channel, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
            {
                Content = new FormUrlEncodedContent(Form(Channel, from, request)),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) =>
            (int)status is >= 200 and < 300
                ? Accepted(ProviderSupport.Text(ProviderSupport.Json(body)?["sid"]))
                : NotificationSendResult.Failed(Translate(status, body));
    }
}

/// <summary>
/// Webhooks de statut de Twilio (corps <c>application/x-www-form-urlencoded</c>), authentifiés par le mot de passe du
/// compte en Basic dans l'URL déclarée (callback du Messaging Service ou du numéro). <c>delivered</c>/<c>read</c> =
/// remis ; <c>undelivered</c>/<c>failed</c> = échec (définitif pour un numéro invalide ou désabonné).
/// </summary>
public sealed class TwilioWebhookReader(string code = TwilioSenderFactory.Code) : INotificationWebhookReader
{
    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(code);

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        if (!WebhookAuth.BasicMatches(headers, credentials, TwilioSenderFactory.WebhookPasswordField))
        {
            return null;
        }

        var form = WebhookAuth.Form(body);
        if (!form.TryGetValue("MessageSid", out var sid) || !form.TryGetValue("MessageStatus", out var status) || sid.Length == 0)
        {
            return [];
        }

        form.TryGetValue("ErrorCode", out var errorCode);
        var (kind, failure, permanent) = status switch
        {
            "delivered" or "read" => (WebhookEventKind.Delivered, (string?)null, false),
            "undelivered" or "failed" => errorCode switch
            {
                "30003" or "30005" or "30006" or "21211" => (WebhookEventKind.Failed, NotificationFailure.InvalidPhoneNumber, true),
                "21610" or "30004" => (WebhookEventKind.Failed, NotificationFailure.Unsubscribed, true),
                _ => (WebhookEventKind.Failed, ProviderSupport.Code("TWILIO", errorCode is { Length: > 0 } ? errorCode : status), false),
            },
            _ => (WebhookEventKind.Ignored, (string?)null, false),
        };
        return [new ProviderWebhookEvent($"{sid}:{status}", kind, sid, failure, permanent)];
    }
}

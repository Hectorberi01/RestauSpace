using System.Globalization;
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
/// Email par l'API Send v3.1 de Mailjet, sur le compte du restaurant (ou de la plateforme) : clé API et clé secrète,
/// adresse et nom d'expéditeur (adresse validée chez Mailjet). Contrairement au SMTP, Mailjet confirme la remise par son
/// webhook d'événements (<see cref="MailjetWebhookReader"/>) : « sent » = remis, « bounce »/« blocked » = échec.
/// </summary>
internal sealed class MailjetSenderFactory(HttpClient http) : INotificationSenderFactory
{
    public const string Code = "MAILJET";
    public const string ApiKeyField = "api_key";
    public const string SecretKeyField = "secret_key";
    public const string FromEmailField = "from_email";
    public const string FromNameField = "from_name";
    public const string WebhookPasswordField = "webhook_password";
    public const string SendUrl = "https://api.mailjet.com/v3.1/send";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Email];

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKeyField, SecretKeyField, FromEmailField, FromNameField, WebhookPasswordField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        string Read(string field) => credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"The Mailjet account has no {field}.");

        var from = Read(FromEmailField);
        if (!from.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Mailjet sender address is invalid.");
        }

        return new MailjetSender(Provider, http, Read(ApiKeyField), Read(SecretKeyField), from, Read(FromNameField));
    }

    internal static JsonObject Payload(string fromEmail, string fromName, NotificationSendRequest request)
    {
        var message = new JsonObject
        {
            ["From"] = new JsonObject { ["Email"] = fromEmail, ["Name"] = fromName },
            ["To"] = new JsonArray(new JsonObject { ["Email"] = request.Recipient.Address }),
            ["Subject"] = request.Content.Subject ?? string.Empty,
            ["CustomID"] = request.DeliveryId.Value.ToString("N"),
        };
        message[request.Content.IsHtml ? "HTMLPart" : "TextPart"] = request.Content.Body;
        return new JsonObject { ["Messages"] = new JsonArray(message) };
    }

    /// <summary>
    /// Traduit une réponse d'erreur de Mailjet. Seul le code d'erreur (<c>mj-…</c>) est lu, jamais le message : il peut
    /// citer l'adresse du destinataire.
    /// </summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string? errorCode, string? errorRelatedTo)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return NotificationFailure.Temporary(NotificationFailure.RateLimited);
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return NotificationFailure.Temporary("PROVIDER_AUTH_FAILED");
        }

        if ((int)status >= 500)
        {
            return NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable);
        }

        if (errorRelatedTo is { } field && field.Contains("To", StringComparison.Ordinal))
        {
            return NotificationFailure.Permanent(NotificationFailure.InvalidEmail);
        }

        if (errorRelatedTo is { } sender && sender.Contains("From", StringComparison.Ordinal))
        {
            return NotificationFailure.Temporary("SENDER_REJECTED");
        }

        return errorCode is { Length: > 0 and <= 20 } code && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? NotificationFailure.Permanent("MJ_" + code.ToUpperInvariant().Replace('-', '_'))
            : NotificationFailure.Permanent(NotificationFailure.Rejected);
    }

    private sealed class MailjetSender(
        NotificationProviderCode provider, HttpClient http, string apiKey, string secretKey, string fromEmail, string fromName) : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => NotificationChannel.Email;

        public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            using var message = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(Payload(fromEmail, fromName, request).ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}")));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.Timeout));
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonNode? root = null;
                try
                {
                    root = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    root = null;
                }

                var first = (root?["Messages"] as JsonArray)?.FirstOrDefault();
                if (response.IsSuccessStatusCode && first?["Status"]?.GetValue<string>() == "success")
                {
                    var id = (first["To"] as JsonArray)?.FirstOrDefault()?["MessageID"];
                    var messageId = id is JsonValue value
                        ? value.TryGetValue<long>(out var number) ? number.ToString(CultureInfo.InvariantCulture) : value.ToString()
                        : null;
                    return NotificationSendResult.Accepted(messageId is { Length: > 0 } ? ProviderMessageId.Create(messageId) : null);
                }

                var error = (first?["Errors"] as JsonArray)?.FirstOrDefault();
                var related = error?["ErrorRelatedTo"] is JsonArray fields ? string.Join(',', fields.Select(field => field?.ToString())) : null;
                return NotificationSendResult.Failed(Translate(
                    response.IsSuccessStatusCode ? HttpStatusCode.BadRequest : response.StatusCode, error?["ErrorCode"]?.ToString(), related));
            }
        }
    }
}

/// <summary>
/// Webhook d'événements de Mailjet. Mailjet ne signe pas : l'URL déclarée chez Mailjet porte un mot de passe en
/// authentification Basic (<c>https://mailjet:&lt;webhook_password&gt;@…</c>), comparé à temps constant à celui du compte.
/// Corps : un événement ou un tableau d'événements (envoi groupé). <c>sent</c> = remis au destinataire ;
/// <c>bounce</c> (définitif si <c>hard_bounce</c>) et <c>blocked</c> = échec ; ouvertures, clics, plaintes et
/// désabonnements sont gardés sans effet sur le statut.
/// </summary>
public sealed class MailjetWebhookReader : INotificationWebhookReader
{
    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(MailjetSenderFactory.Code);

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!credentials.TryGetValue(MailjetSenderFactory.WebhookPasswordField, out var expected) || string.IsNullOrEmpty(expected)
            || Password(headers) is not { } given
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
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
                var kindText = Text(item, "event");
                var messageId = Text(item, "MessageID");
                if (string.IsNullOrEmpty(kindText) || string.IsNullOrEmpty(messageId))
                {
                    continue;
                }

                var hard = item.TryGetProperty("hard_bounce", out var hardValue) && hardValue.ValueKind == JsonValueKind.True;
                var (kind, failure, permanent) = kindText switch
                {
                    "sent" => (WebhookEventKind.Delivered, (string?)null, false),
                    "bounce" => (WebhookEventKind.Failed, hard ? NotificationFailure.InvalidEmail : "SOFT_BOUNCE", hard),
                    "blocked" => (WebhookEventKind.Failed, NotificationFailure.Rejected, true),
                    _ => (WebhookEventKind.Ignored, (string?)null, false),
                };
                events.Add(new ProviderWebhookEvent($"{messageId}:{kindText}:{Text(item, "time")}", kind, messageId, failure, permanent));
            }

            return events;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Le mot de passe de l'en-tête <c>Authorization: Basic</c>, ou null.</summary>
    private static string? Password(IReadOnlyDictionary<string, string> headers)
    {
        var header = headers.TryGetValue("Authorization", out var value)
            ? value
            : headers.FirstOrDefault(pair => string.Equals(pair.Key, "Authorization", StringComparison.OrdinalIgnoreCase)).Value;
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

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;
}

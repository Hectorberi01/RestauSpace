using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// WhatsApp Business Cloud API, directement chez Meta. Un message à l'initiative du restaurant (confirmation,
/// rappel...) DOIT passer par un modèle approuvé par Meta : le compte désigne UN modèle générique dont le corps est
/// <c>{{1}}</c>, rempli avec le message rendu par le module. Les retours à la ligne sont remplacés (Meta les refuse
/// dans un paramètre). Remise et lecture arrivent ensuite par webhook (<see cref="Webhooks.WhatsAppMetaWebhookReader"/>).
/// </summary>
internal sealed partial class WhatsAppMetaSenderFactory(HttpClient http, WhatsAppMetaOptions options) : INotificationSenderFactory
{
    public const string Code = "WHATSAPP_META";
    public const string AccessTokenField = "access_token";
    public const string PhoneNumberIdField = "phone_number_id";
    public const string AppSecretField = "app_secret";
    public const string VerifyTokenField = "verify_token";
    public const string TemplateNameField = "template_name";
    public const string TemplateLanguageField = "template_language";

    /// <summary>Longueur maximale du paramètre de modèle accepté par Meta.</summary>
    public const int MaxParameterLength = 1024;

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.WhatsApp];

    public IReadOnlyList<string> CredentialFields { get; } =
        [AccessTokenField, PhoneNumberIdField, AppSecretField, VerifyTokenField, TemplateNameField, TemplateLanguageField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        string Read(string field) => credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"The WhatsApp account has no {field}.");

        var phoneNumberId = Read(PhoneNumberIdField);
        if (!phoneNumberId.All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException("The WhatsApp phone number id must be numeric.");
        }

        return new WhatsAppMetaSender(
            Provider, http, options, Read(AccessTokenField), phoneNumberId, Read(TemplateNameField), Read(TemplateLanguageField));
    }

    /// <summary>Le message rendu, en une ligne acceptable comme paramètre de modèle.</summary>
    internal static string ToParameter(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var single = string.Join(" · ", lines).Replace('\t', ' ');
        return Spaces().Replace(single, "   ");
    }

    [GeneratedRegex(" {4,}")]
    private static partial Regex Spaces();

    internal static object Payload(string to, string template, string language, string text) => new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to,
        type = "template",
        template = new
        {
            name = template,
            language = new { code = language },
            components = new object[]
            {
                new { type = "body", parameters = new object[] { new { type = "text", text } } },
            },
        },
    };

    /// <summary>
    /// Traduit une erreur de l'API Graph. Seul le code numérique est repris (préfixé <c>WA_</c>), jamais le message :
    /// il peut citer le numéro du destinataire.
    /// </summary>
    internal static NotificationFailure Translate(HttpStatusCode status, int? code)
    {
        if (status == HttpStatusCode.TooManyRequests || code is 4 or 80007 or 130429 or 131048 or 131056)
        {
            return NotificationFailure.Temporary(NotificationFailure.RateLimited);
        }

        if (status == HttpStatusCode.Unauthorized || code is 190 or 0 or 10 or 200)
        {
            return NotificationFailure.Temporary("PROVIDER_AUTH_FAILED");
        }

        if (code is 131026 or 131021 or 131030)
        {
            return NotificationFailure.Permanent(NotificationFailure.InvalidRecipient);
        }

        if (code is 132000 or 132001 or 132005 or 132007 or 132012 or 132015 or 132016)
        {
            return NotificationFailure.Permanent("TEMPLATE_INVALID");
        }

        if ((int)status >= 500 || code is 1 or 2 or 131000 or 131016)
        {
            return NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable);
        }

        return code is { } known
            ? NotificationFailure.Permanent($"WA_{known}")
            : NotificationFailure.Permanent(NotificationFailure.Rejected);
    }

    private sealed class WhatsAppMetaSender(
        NotificationProviderCode provider, HttpClient http, WhatsAppMetaOptions options, string accessToken, string phoneNumberId,
        string templateName, string templateLanguage) : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => NotificationChannel.WhatsApp;

        public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var text = ToParameter(request.Content.Body);
            if (text.Length > MaxParameterLength)
            {
                return NotificationSendResult.Failed(NotificationFailure.Permanent("CONTENT_TOO_LONG"));
            }

            // Meta attend le numéro sans « + ».
            var to = request.Recipient.Address.TrimStart('+');
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.GraphUrl.TrimEnd('/')}/{options.ApiVersion}/{phoneNumberId}/messages")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(Payload(to, templateName, templateLanguage, text)), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var response = await http.SendAsync(message, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode && MessageId(body) is { } id)
                {
                    return NotificationSendResult.Accepted(ProviderMessageId.Create(id));
                }

                return NotificationSendResult.Failed(Translate(response.StatusCode, ErrorCode(body)));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return NotificationSendResult.Failed(exception is TaskCanceledException or TimeoutException
                    ? NotificationFailure.Temporary(NotificationFailure.Timeout)
                    : NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable));
            }
        }

        private static string? MessageId(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("messages", out var messages)
                    && messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0
                    && messages[0].TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 and <= ProviderMessageId.MaxLength } value
                        ? value
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int? ErrorCode(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("error", out var error)
                    && error.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)
                        ? value
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary>Adresse et version de l'API Graph de Meta (réglables par l'hôte, par exemple pour suivre une nouvelle version).</summary>
public sealed class WhatsAppMetaOptions
{
    public string GraphUrl { get; set; } = "https://graph.facebook.com";

    public string ApiVersion { get; set; } = "v23.0";
}

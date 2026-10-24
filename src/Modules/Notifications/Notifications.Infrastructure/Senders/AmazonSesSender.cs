using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Amazon SES (API v2), email à gros volume : région, clé d'accès IAM (droit <c>ses:SendEmail</c> seulement), expéditeur
/// vérifié chez SES. Requêtes signées AWS Signature V4, sans SDK. Les rebonds de SES passent par Amazon SNS, pas encore
/// relié : un email accepté reste « envoyé ».
/// </summary>
internal sealed partial class AmazonSesSenderFactory(HttpClient http, IClock clock) : INotificationSenderFactory
{
    public const string Code = "SES";
    public const string RegionField = "region";
    public const string AccessKeyField = "access_key_id";
    public const string SecretKeyField = "secret_access_key";
    public const string FromEmailField = "from_email";
    public const string FromNameField = "from_name";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Email];

    public IReadOnlyList<string> CredentialFields { get; } = [RegionField, AccessKeyField, SecretKeyField, FromEmailField, FromNameField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        var region = ProviderSupport.Read(credentials, RegionField, "Amazon SES").ToLowerInvariant();
        if (!RegionShape().IsMatch(region))
        {
            throw new InvalidOperationException("The Amazon SES region is invalid.");
        }

        return new SesSender(
            Provider, http, clock, region, ProviderSupport.Read(credentials, AccessKeyField, "Amazon SES"),
            ProviderSupport.Read(credentials, SecretKeyField, "Amazon SES"), ProviderSupport.ReadSender(credentials, FromEmailField, "Amazon SES"),
            ProviderSupport.Read(credentials, FromNameField, "Amazon SES"));
    }

    // Une vraie région AWS (eu-west-3, af-south-1…) : l'hôte appelé en dépend, rien d'autre n'y entre.
    [GeneratedRegex("^[a-z]{2}(-gov)?-[a-z]+-[0-9]$")]
    private static partial Regex RegionShape();

    internal static JsonObject Payload(string fromEmail, string fromName, NotificationSendRequest request)
    {
        var body = new JsonObject { ["Data"] = request.Content.Body, ["Charset"] = "UTF-8" };
        return new JsonObject
        {
            ["FromEmailAddress"] = $"\"{fromName.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{fromEmail}>",
            ["Destination"] = new JsonObject { ["ToAddresses"] = new JsonArray(request.Recipient.Address) },
            ["Content"] = new JsonObject
            {
                ["Simple"] = new JsonObject
                {
                    ["Subject"] = new JsonObject { ["Data"] = request.Content.Subject ?? string.Empty, ["Charset"] = "UTF-8" },
                    ["Body"] = new JsonObject { [request.Content.IsHtml ? "Html" : "Text"] = body },
                },
            },
            ["EmailTags"] = new JsonArray(new JsonObject { ["Name"] = "delivery", ["Value"] = request.DeliveryId.Value.ToString("N") }),
        };
    }

    /// <summary>
    /// Signature AWS V4 d'un POST JSON : en-têtes <c>host</c>, <c>x-amz-date</c> et <c>Authorization</c>. Le secret ne sort
    /// jamais de ce calcul.
    /// </summary>
    internal static void Sign(HttpRequestMessage message, string payload, string region, string accessKey, string secretKey, DateTimeOffset now)
    {
        var uri = message.RequestUri!;
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = amzDate[..8];
        var payloadHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        const string signedHeaders = "content-type;host;x-amz-date";
        var canonical = string.Join('\n',
            "POST", uri.AbsolutePath, string.Empty,
            "content-type:application/json", $"host:{uri.Host}", $"x-amz-date:{amzDate}", string.Empty,
            signedHeaders, payloadHash);
        var scope = $"{day}/{region}/ses/aws4_request";
        var toSign = string.Join('\n', "AWS4-HMAC-SHA256", amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretKey), Encoding.UTF8.GetBytes(day));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, "ses"u8.ToArray());
        key = HMACSHA256.HashData(key, "aws4_request"u8.ToArray());
        var signature = Hex(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(toSign)));
        message.Headers.Add("x-amz-date", amzDate);
        message.Headers.TryAddWithoutValidation(
            "Authorization", $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>Le type d'erreur de SES (<c>MessageRejected</c>, <c>AccountSuspendedException</c>…), jamais son message.</summary>
    internal static NotificationFailure Translate(HttpStatusCode status, string body)
    {
        if (ProviderSupport.FromStatus(status) is { } common)
        {
            return common;
        }

        var json = ProviderSupport.Json(body);
        var type = ProviderSupport.Text(json?["__type"]) ?? ProviderSupport.Text(json?["code"]);
        type = type?.Split('#').Last();
        return type switch
        {
            "TooManyRequestsException" or "LimitExceededException" => NotificationFailure.Temporary(NotificationFailure.RateLimited),
            "AccountSuspendedException" or "SendingPausedException" or "MailFromDomainNotVerifiedException" => NotificationFailure.Temporary("SENDER_REJECTED"),
            "MessageRejected" or "MessageRejectedException" => NotificationFailure.Permanent(NotificationFailure.Rejected),
            _ => NotificationFailure.Permanent(ProviderSupport.Code("SES", type)),
        };
    }

    private sealed class SesSender(
        NotificationProviderCode provider, HttpClient http, IClock clock, string region, string accessKey, string secretKey, string fromEmail, string fromName)
        : HttpNotificationSender(provider, NotificationChannel.Email, http)
    {
        protected override Task<HttpRequestMessage> BuildAsync(NotificationSendRequest request, CancellationToken cancellationToken)
        {
            var payload = Payload(fromEmail, fromName, request).ToJsonString();
            var message = new HttpRequestMessage(HttpMethod.Post, $"https://email.{region}.amazonaws.com/v2/email/outbound-emails")
            {
                Content = new StringContent(payload, Encoding.UTF8),
            };
            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            Sign(message, payload, region, accessKey, secretKey, clock.UtcNow);
            return Task.FromResult(message);
        }

        protected override NotificationSendResult Read(HttpStatusCode status, string body) =>
            (int)status is >= 200 and < 300
                ? Accepted(ProviderSupport.Text(ProviderSupport.Json(body)?["MessageId"]))
                : NotificationSendResult.Failed(Translate(status, body));
    }
}

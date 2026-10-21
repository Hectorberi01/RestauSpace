using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Senders;

namespace Notifications.Infrastructure.Webhooks;

/// <summary>Ce qu'un webhook dit, normalisé (§ 29).</summary>
public sealed record ProviderWebhookEvent(string EventId, WebhookEventKind Kind, string? ProviderMessageId, string? FailureCode, bool IsPermanentFailure);

/// <summary>
/// Lit les webhooks d'UN prestataire : vérifie leur signature avec le secret du compte, puis traduit leur contenu.
/// Une signature fausse = rien n'est lu (null). Une implémentation par prestataire, comme les senders.
/// </summary>
public interface INotificationWebhookReader
{
    NotificationProviderCode Provider { get; }

    IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials);
}

/// <summary>
/// Webhooks du prestataire simulé : en-tête <c>X-Fake-Signature</c> = HMAC-SHA256 hexadécimal du corps avec le
/// <c>webhook_secret</c> du compte ; corps <c>{"events":[{"id","messageId","kind","failureCode","permanent"}]}</c>,
/// <c>kind</c> parmi delivered, partially_delivered, failed, opened, clicked, unsubscribed.
/// </summary>
public sealed class FakeWebhookReader : INotificationWebhookReader
{
    public const string SignatureHeader = "X-Fake-Signature";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(FakeSenderFactory.Code);

    public static string Sign(string secret, byte[] body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        if (!credentials.TryGetValue(FakeSenderFactory.WebhookSecretField, out var secret)
            || !headers.TryGetValue(SignatureHeader, out var signature))
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, body.ToArray()));
        var given = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<FakeWebhookBody>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (document?.Events ?? new List<FakeWebhookItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new ProviderWebhookEvent(
                    item.Id!,
                    item.Kind switch
                    {
                        "delivered" => WebhookEventKind.Delivered,
                        "partially_delivered" => WebhookEventKind.PartiallyDelivered,
                        "failed" or "bounced" => WebhookEventKind.Failed,
                        _ => WebhookEventKind.Ignored,
                    },
                    item.MessageId,
                    item.FailureCode,
                    item.Permanent))
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FakeWebhookBody(List<FakeWebhookItem>? Events);

    private sealed record FakeWebhookItem(string? Id, string? MessageId, string? Kind, string? FailureCode, bool Permanent);
}

/// <summary>Réponse au prestataire : 2xx dès que le webhook est conservé (ou déjà reçu), 401 si sa signature est fausse.</summary>
public enum WebhookReception
{
    Accepted = 1,
    Rejected = 2,
    UnknownProvider = 3,
}

/// <summary>
/// Reçoit un webhook (route anonyme : l'authenticité vient de la SIGNATURE) : retrouve le compte qui l'a signé (celui de
/// la plateforme, ou celui de la configuration du restaurant désignée par l'URL), vérifie, puis range les événements
/// dans <c>provider_webhook_events</c> (un événement renvoyé n'est rangé qu'une fois). Appliqués ensuite par le worker.
/// Le corps brut n'est pas gardé (adresse, contenu).
/// </summary>
public sealed class NotificationWebhookReceiver
{
    private readonly NotificationsDbContext _context;
    private readonly IReadOnlyDictionary<NotificationProviderCode, INotificationWebhookReader> _readers;
    private readonly IReadOnlyDictionary<NotificationProviderCode, INotificationWebhookChallenge> _challenges;
    private readonly INotificationSecretStore _secrets;
    private readonly IClock _clock;
    private readonly ILogger<NotificationWebhookReceiver> _logger;

    public NotificationWebhookReceiver(
        NotificationsDbContext context,
        IEnumerable<INotificationWebhookReader> readers,
        IEnumerable<INotificationWebhookChallenge> challenges,
        INotificationSecretStore secrets,
        IClock clock,
        ILogger<NotificationWebhookReceiver> logger)
    {
        _context = context;
        _readers = readers.ToDictionary(reader => reader.Provider);
        _challenges = challenges.ToDictionary(challenge => challenge.Provider);
        _secrets = secrets;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WebhookReception> ReceiveAsync(
        string provider, Guid? configurationId, IReadOnlyDictionary<string, string> headers, byte[] body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);
        NotificationProviderCode code;
        try
        {
            code = NotificationProviderCode.Create(provider);
        }
        catch (DomainException)
        {
            return WebhookReception.UnknownProvider;
        }

        if (!_readers.TryGetValue(code, out var reader))
        {
            return WebhookReception.UnknownProvider;
        }

        var reference = await ReferenceAsync(code, configurationId, cancellationToken);
        var credentials = reference is null ? null : await _secrets.ReadAsync(reference, cancellationToken);
        var events = credentials is null ? null : reader.Read(headers, body, credentials);
        if (events is null)
        {
            // Ni le corps ni l'en-tête : seulement le prestataire.
            _logger.LogWarning("Notification webhook rejected for provider {Provider}: signature or account invalid.", code.Value);
            return WebhookReception.Rejected;
        }

        var now = _clock.UtcNow;
        foreach (var item in events)
        {
            var eventId = item.EventId.Length > NotificationWebhookEvent.MaxEventIdLength ? item.EventId[..NotificationWebhookEvent.MaxEventIdLength] : item.EventId;
            var messageId = item.ProviderMessageId is { Length: > 0 and <= ProviderMessageId.MaxLength } id ? id : null;
            var failureCode = item.FailureCode is { Length: > 0 } failure ? failure[..Math.Min(failure.Length, NotificationFailure.MaxCodeLength)] : null;
            var kind = (int)item.Kind;
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO notifications.provider_webhook_events
                     (provider, event_id, kind, provider_message_id, failure_code, is_permanent_failure, received_at_utc, attempts, next_attempt_at_utc)
                 VALUES ({code.Value}, {eventId}, {kind}, {messageId}, {failureCode}, {item.IsPermanentFailure}, {now}, 0, {now})
                 ON CONFLICT (provider, event_id) DO NOTHING
                 """,
                cancellationToken);
        }

        return WebhookReception.Accepted;
    }

    /// <summary>
    /// Vérification d'abonnement d'un prestataire (requête GET) : la réponse attendue si le jeton du compte correspond,
    /// sinon null. Ni le jeton ni la requête ne sont journalisés.
    /// </summary>
    public async Task<string?> AnswerChallengeAsync(
        string provider, Guid? configurationId, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NotificationProviderCode code;
        try
        {
            code = NotificationProviderCode.Create(provider);
        }
        catch (DomainException)
        {
            return null;
        }

        if (!_challenges.TryGetValue(code, out var challenge))
        {
            return null;
        }

        var reference = await ReferenceAsync(code, configurationId, cancellationToken);
        var credentials = reference is null ? null : await _secrets.ReadAsync(reference, cancellationToken);
        var answer = credentials is null ? null : challenge.Answer(query, credentials);
        if (answer is null)
        {
            _logger.LogWarning("Notification webhook subscription refused for provider {Provider}.", code.Value);
        }

        return answer;
    }

    /// <summary>Le compte qui signe : celui de la configuration (mode restaurant) désignée par l'URL, sinon celui de la plateforme.</summary>
    private async Task<NotificationCredentialReference?> ReferenceAsync(
        NotificationProviderCode code, Guid? configurationId, CancellationToken cancellationToken)
    {
        if (configurationId is not { } id || id == Guid.Empty)
        {
            return NotificationCredentialReference.ForPlatform(code);
        }

        var configuration = await _context.Configurations.IgnoreQueryFilters().AsNoTracking()
            .Where(candidate => candidate.Id == RestaurantNotificationConfigurationId.From(id) && candidate.Provider == code)
            .Select(candidate => new { candidate.Mode, candidate.SecretReference })
            .SingleOrDefaultAsync(cancellationToken);
        return configuration switch
        {
            null => null,
            { Mode: NotificationProviderMode.TenantManaged } => configuration.SecretReference,
            _ => NotificationCredentialReference.ForPlatform(code),
        };
    }
}

/// <summary>
/// Vérification d'abonnement demandée par certains prestataires avant d'envoyer leurs webhooks (Meta : requête GET avec
/// <c>hub.mode</c>, <c>hub.verify_token</c> et <c>hub.challenge</c>). Renvoie la réponse attendue, ou null si le jeton
/// ne correspond pas au compte.
/// </summary>
public interface INotificationWebhookChallenge
{
    NotificationProviderCode Provider { get; }

    string? Answer(IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> credentials);
}

/// <summary>
/// Webhooks de WhatsApp Cloud API : en-tête <c>X-Hub-Signature-256</c> = <c>sha256=</c> + HMAC-SHA256 hexadécimal du corps
/// avec le <c>app_secret</c> du compte. Seuls les statuts de messages sont lus (<c>delivered</c>, <c>read</c>, <c>failed</c>) ;
/// les messages reçus des clients sont ignorés. Aucun numéro ni contenu n'est conservé.
/// </summary>
public sealed class WhatsAppMetaWebhookReader : INotificationWebhookReader, INotificationWebhookChallenge
{
    public const string SignatureHeader = "X-Hub-Signature-256";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(WhatsAppMetaSenderFactory.Code);

    public static string Sign(string secret, byte[] body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    public string? Answer(IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!query.TryGetValue("hub.mode", out var mode) || mode != "subscribe"
            || !query.TryGetValue("hub.verify_token", out var token)
            || !query.TryGetValue("hub.challenge", out var challenge) || string.IsNullOrEmpty(challenge) || challenge.Length > 256
            || !credentials.TryGetValue(WhatsAppMetaSenderFactory.VerifyTokenField, out var expected))
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)) ? challenge : null;
    }

    public IReadOnlyList<ProviderWebhookEvent>? Read(
        IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!credentials.TryGetValue(WhatsAppMetaSenderFactory.AppSecretField, out var secret)
            || !headers.TryGetValue(SignatureHeader, out var signature))
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, body.ToArray()));
        var given = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var events = new List<ProviderWebhookEvent>();
            foreach (var status in Statuses(document.RootElement))
            {
                var id = status.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var state = status.TryGetProperty("status", out var stateValue) ? stateValue.GetString() : null;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(state))
                {
                    continue;
                }

                int? code = null;
                if (status.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                    && errors[0].TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var parsed))
                {
                    code = parsed;
                }

                var kind = state switch
                {
                    "delivered" or "read" => WebhookEventKind.Delivered,
                    "failed" => WebhookEventKind.Failed,
                    _ => WebhookEventKind.Ignored,
                };
                var failure = kind == WebhookEventKind.Failed ? WhatsAppMetaSenderFactory.Translate(System.Net.HttpStatusCode.OK, code) : null;
                events.Add(new ProviderWebhookEvent($"{id}:{state}", kind, id, failure?.Code, failure?.IsPermanent ?? false));
            }

            return events;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Statuses(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value) && value.TryGetProperty("statuses", out var statuses)
                    && statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray())
                    {
                        yield return status;
                    }
                }
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// L'intégration d'UN prestataire dans le code : ses canaux, les champs de compte qu'elle attend, et la fabrique de ses
/// senders. Ajouter Resend, Twilio, WhatsApp Cloud, Firebase ou APNs = ajouter une classe ici et l'enregistrer ; rien ne
/// change dans le Domain ni dans l'Application.
/// </summary>
public interface INotificationSenderFactory
{
    NotificationProviderCode Provider { get; }

    IReadOnlyList<NotificationChannel> Channels { get; }

    IReadOnlyList<string> CredentialFields { get; }

    /// <param name="credentials">Les identifiants du compte, en clair, en mémoire seulement le temps de l'envoi.</param>
    INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials);

    /// <summary>Compte saisi par un restaurant (mode TenantManaged) : l'intégration peut y appliquer des règles plus strictes.</summary>
    INotificationSender CreateForTenant(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) =>
        Create(channel, credentials);
}

/// <summary>
/// (prestataire + canal + compte) -> sender, par un dictionnaire des intégrations (aucun aiguillage géant). Les
/// identifiants sont lus au coffre à chaque résolution selon le mode : compte de la plateforme
/// (<c>notifications/platform/{code}</c>) ou du restaurant (la référence de SA configuration). Ils ne sont jamais
/// gardés, jamais journalisés.
/// </summary>
internal sealed class NotificationSenderResolver : INotificationSenderResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoCredentials = new Dictionary<string, string>().ToFrozenDictionary();

    private readonly FrozenDictionary<NotificationProviderCode, INotificationSenderFactory> _factories;
    private readonly INotificationSecretStore _secrets;

    public NotificationSenderResolver(IEnumerable<INotificationSenderFactory> factories, INotificationSecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _secrets = secrets;
        var list = factories.ToList();
        var duplicate = list.GroupBy(factory => factory.Provider).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two notification integrations are registered for provider {duplicate.Key}.");
        }

        _factories = list.ToFrozenDictionary(factory => factory.Provider);
        Integrations = list
            .Select(factory => new NotificationIntegration(factory.Provider, factory.Channels, factory.CredentialFields))
            .OrderBy(integration => integration.Provider.Value, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<NotificationIntegration> Integrations { get; }

    public NotificationIntegration? FindIntegration(NotificationProviderCode provider) =>
        Integrations.FirstOrDefault(integration => integration.Provider == provider);

    public async Task<INotificationSender?> ResolveAsync(
        NotificationProviderCode provider,
        NotificationChannel channel,
        RestaurantNotificationConfiguration? configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_factories.TryGetValue(provider, out var factory) || !factory.Channels.Contains(channel))
        {
            return null;
        }

        if (factory.CredentialFields.Count == 0)
        {
            return factory.Create(channel, NoCredentials);
        }

        var tenantManaged = configuration is { Mode: NotificationProviderMode.TenantManaged };
        var reference = tenantManaged ? configuration!.SecretReference : NotificationCredentialReference.ForPlatform(provider);
        var credentials = reference is null ? null : await _secrets.ReadAsync(reference, cancellationToken);
        if (credentials is null)
        {
            return null;
        }

        return tenantManaged ? factory.CreateForTenant(channel, credentials) : factory.Create(channel, credentials);
    }
}

/// <summary>
/// Prestataire INTERNE des notifications in-app : la notification enregistrée EST affichée dans la boîte de
/// l'utilisateur, donc remise dès l'envoi. Aucun compte, aucun appel réseau.
/// </summary>
internal sealed class InAppSenderFactory : INotificationSenderFactory
{
    public NotificationProviderCode Provider => NotificationProviderCode.InApp;

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.InApp];

    public IReadOnlyList<string> CredentialFields { get; } = [];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials) => new InAppSender();

    private sealed class InAppSender : INotificationSender
    {
        public NotificationProviderCode Provider => NotificationProviderCode.InApp;

        public NotificationChannel Channel => NotificationChannel.InApp;

        public Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationSendResult.Delivered());
    }
}

/// <summary>Un message « envoyé » par le prestataire simulé, gardé EN MÉMOIRE (développement et tests uniquement).</summary>
public sealed record FakeSentMessage(
    NotificationId NotificationId, NotificationChannel Channel, string Address, string? Subject, string Body, ProviderMessageId MessageId);

/// <summary>
/// La boîte d'envoi du prestataire simulé <c>FAKE</c> : ce qui a été « envoyé », et les réponses à donner aux prochains
/// envois (échec temporaire, permanent, exception). Singleton.
/// </summary>
public sealed class FakeNotificationOutbox
{
    private readonly ConcurrentQueue<Func<NotificationSendResult>> _scripted = new();
    private readonly ConcurrentQueue<FakeSentMessage> _sent = new();

    public IReadOnlyList<FakeSentMessage> Sent => _sent.ToList();

    /// <summary>Le prochain envoi échoue ainsi (file : un échec par appel).</summary>
    public void FailNext(NotificationFailure failure) => _scripted.Enqueue(() => NotificationSendResult.Failed(failure));

    /// <summary>Le prochain envoi lève une exception (prestataire injoignable).</summary>
    public void ThrowNext() => _scripted.Enqueue(() => throw new HttpRequestException("fake provider unreachable"));

    internal NotificationSendResult Send(NotificationSendRequest request)
    {
        if (_scripted.TryDequeue(out var scripted))
        {
            return scripted();
        }

        var messageId = ProviderMessageId.Create("fake-" + Guid.NewGuid().ToString("N"));
        _sent.Enqueue(new FakeSentMessage(
            request.NotificationId, request.Channel, request.Recipient.Address, request.Content.Subject, request.Content.Body, messageId));
        return NotificationSendResult.Accepted(messageId);
    }
}

/// <summary>
/// Prestataire SIMULÉ <c>FAKE</c> (§ 45, étape 15) sur tous les canaux externes : rien ne sort, tout est gardé en
/// mémoire. Attend un compte (<c>api_key</c>, <c>webhook_secret</c>) comme un vrai prestataire, pour éprouver le
/// coffre et les webhooks signés. Jamais en production.
/// </summary>
internal sealed class FakeSenderFactory(FakeNotificationOutbox outbox) : INotificationSenderFactory
{
    public const string Code = "FAKE";
    public const string ApiKeyField = "api_key";
    public const string WebhookSecretField = "webhook_secret";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } =
        [NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.WhatsApp, NotificationChannel.Push];

    public IReadOnlyList<string> CredentialFields { get; } = [ApiKeyField, WebhookSecretField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (!credentials.TryGetValue(ApiKeyField, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("The fake provider account has no API key.");
        }

        return new FakeSender(Provider, channel, outbox);
    }

    private sealed class FakeSender(NotificationProviderCode provider, NotificationChannel channel, FakeNotificationOutbox outbox) : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => channel;

        public Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.FromResult(outbox.Send(request));
        }
    }
}

/// <summary>Quotas par défaut, en attendant Billing / Entitlements : la même limite mensuelle pour tous les restaurants (null = sans limite).</summary>
internal sealed class ConfiguredNotificationEntitlements(NotificationsQuotaOptions options) : INotificationEntitlementChecker
{
    public Task<long?> MonthlyLimitAsync(TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult(options.MonthlyLimits.TryGetValue(channel, out var limit) ? (long?)limit : null);
}

/// <summary>Limites mensuelles par canal appliquées par <see cref="ConfiguredNotificationEntitlements"/>.</summary>
public sealed class NotificationsQuotaOptions
{
    public Dictionary<NotificationChannel, long> MonthlyLimits { get; } = [];
}

/// <summary>Sans hôte (tests, outils) : personne n'est connecté.</summary>
internal sealed class NoCurrentUser : INotificationsCurrentUser
{
    public UserId? UserId => null;
}

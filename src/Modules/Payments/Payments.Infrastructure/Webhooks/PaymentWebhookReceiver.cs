using Npgsql;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Persistence.Configurations;

namespace Payments.Infrastructure.Webhooks;

public enum WebhookReceipt
{
    /// <summary>Vérifié et mis en file : il sera appliqué en arrière-plan.</summary>
    Accepted = 1,

    /// <summary>Déjà reçu (le prestataire renvoie) : rien de plus.</summary>
    Duplicate = 2,

    /// <summary>Authentique mais sans intérêt pour nous.</summary>
    Ignored = 3,

    /// <summary>Adresse inconnue : pas de telle configuration en ligne, pas d'intégration, ou pas d'identifiants.</summary>
    UnknownEndpoint = 4,

    /// <summary>Signature absente, fausse ou expirée, ou corps illisible.</summary>
    Invalid = 5,

    /// <summary>Le prestataire n'a pas pu confirmer la transaction pour l'instant : à renvoyer plus tard.</summary>
    Unavailable = 6,
}

/// <summary>
/// Première étape d'un webhook : chaque configuration a son adresse (<c>/payments/webhooks/{prestataire}/{configuration}</c>),
/// car chaque compte marchand signe avec SON secret. On retrouve la configuration, on lit ses identifiants dans le coffre,
/// le lecteur du prestataire vérifie la signature, puis on conserve le webhook. Rien n'est appliqué ici, pour répondre vite au
/// prestataire (qui renvoie tant qu'il n'a pas de 2xx) ; <see cref="PaymentWebhookProcessor"/> s'en charge.
/// </summary>
public sealed class PaymentWebhookReceiver
{
    private readonly Dictionary<PaymentProviderCode, IPaymentIntegration> _integrations;
    private readonly IPaymentSecretStore _secrets;
    private readonly PaymentsDbContext _context;
    private readonly IClock _clock;

    public PaymentWebhookReceiver(
        IEnumerable<IPaymentIntegration> integrations, IPaymentSecretStore secrets, PaymentsDbContext context, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        _integrations = integrations.ToDictionary(integration => integration.Provider);
        _secrets = secrets;
        _context = context;
        _clock = clock;
    }

    /// <summary>
    /// Webhook du compte de la PLATEFORME (<c>/payments/webhooks/{prestataire}</c>) : vérifié avec ses identifiants, rien
    /// n'est conservé ; renvoie la référence de la transaction relue chez le prestataire.
    /// </summary>
    public async Task<(WebhookReceipt Receipt, string? Reference)> ReceivePlatformAsync(
        string provider, IReadOnlyDictionary<string, string> headers, string body, CancellationToken cancellationToken = default)
    {
        PaymentProviderCode code;
        try
        {
            code = PaymentProviderCode.Create(provider);
        }
        catch (DomainException)
        {
            return (WebhookReceipt.UnknownEndpoint, null);
        }

        if (!_integrations.TryGetValue(code, out var integration))
        {
            return (WebhookReceipt.UnknownEndpoint, null);
        }

        IReadOnlyDictionary<string, string>? secrets;
        try
        {
            secrets = await _secrets.ReadAsync(PaymentCredentialReference.ForPlatform(code), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return (WebhookReceipt.Invalid, null);
        }

        if (secrets is null)
        {
            return (WebhookReceipt.UnknownEndpoint, null);
        }

        WebhookReadResult read;
        try
        {
            read = await integration.CreateWebhookReader(secrets).ReadAsync(headers, body, _clock.UtcNow, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            return (WebhookReceipt.Unavailable, null);
        }

        return read switch
        {
            { Status: WebhookReadStatus.Invalid } or { Status: WebhookReadStatus.Verified, Webhook: null } => (WebhookReceipt.Invalid, null),
            { Webhook: { } webhook } => (WebhookReceipt.Accepted, webhook.Reference),
            _ => (WebhookReceipt.Ignored, null),
        };
    }

    public async Task<WebhookReceipt> ReceiveAsync(
        string provider, Guid configurationId, IReadOnlyDictionary<string, string> headers, string body, CancellationToken cancellationToken = default)
    {
        // Aucun tenant dans une requête de prestataire : la configuration est cherchée par son id, la signature dira si
        // elle est bien la sienne.
        RestaurantPaymentConfiguration? configuration = null;
        if (configurationId != Guid.Empty)
        {
            var id = RestaurantPaymentConfigurationId.From(configurationId);
            configuration = await _context.Configurations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        }

        if (configuration is not { ProviderType: PaymentProviderType.Online }
            || !string.Equals(configuration.Provider.Value, provider, StringComparison.Ordinal)
            || CredentialReferences.Of(configuration) is not { } reference
            || !_integrations.TryGetValue(configuration.Provider, out var integration))
        {
            return WebhookReceipt.UnknownEndpoint;
        }

        IReadOnlyDictionary<string, string>? secrets;
        try
        {
            secrets = await _secrets.ReadAsync(reference, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Identifiants illisibles : on ne peut rien vérifier, donc rien accepter.
            return WebhookReceipt.Invalid;
        }

        if (secrets is null)
        {
            return WebhookReceipt.UnknownEndpoint;
        }

        var now = _clock.UtcNow;
        WebhookReadResult read;
        try
        {
            read = await integration.CreateWebhookReader(secrets).ReadAsync(headers, body, now, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // Relecture impossible pour l'instant : le prestataire renverra le webhook tant qu'il n'a pas de 2xx.
            return WebhookReceipt.Unavailable;
        }

        if (read.Status == WebhookReadStatus.Invalid || read is { Status: WebhookReadStatus.Verified, Webhook: null })
        {
            return WebhookReceipt.Invalid;
        }

        if (read.Webhook is not { } webhook)
        {
            return WebhookReceipt.Ignored;
        }

        var entry = _context.Webhooks.Add(new PaymentWebhook(configuration.TenantId, configuration.Id.Value, provider, webhook, body, now));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return WebhookReceipt.Accepted;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: PaymentWebhookConfiguration.EventIndex,
        })
        {
            entry.State = EntityState.Detached;
            return WebhookReceipt.Duplicate;
        }
    }
}

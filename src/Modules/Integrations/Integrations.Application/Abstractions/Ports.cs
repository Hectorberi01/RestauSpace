using Integrations.Domain;

namespace Integrations.Application.Abstractions;

// Ports du module Integrations. Le tenant est TOUJOURS donné explicitement : celui du contexte côté restaurant ; seules
// les lectures du job (IWebhookWorkQueue) et de l'authentification par clé (IApiCredentialLookup) voient tous les tenants.

public interface IWebhookEndpointRepository
{
    Task<WebhookEndpoint?> FindAsync(TenantId tenantId, WebhookEndpointId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Les endpoints ACTIFS du tenant (suivis : une livraison est créée à partir de chacun).</summary>
    Task<IReadOnlyList<WebhookEndpoint>> ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(WebhookEndpoint endpoint);
}

public interface IWebhookDeliveryRepository
{
    Task<WebhookDelivery?> FindAsync(TenantId tenantId, DeliveryId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les livraisons d'un endpoint, des plus récentes aux plus anciennes.</summary>
    Task<IReadOnlyList<WebhookDelivery>> ListAsync(
        TenantId tenantId, WebhookEndpointId endpointId, WebhookDeliveryStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Une livraison non abandonnée existe-t-elle déjà pour (endpoint, événement) ? (écritures en cours comprises)</summary>
    Task<bool> HasLiveDeliveryAsync(TenantId tenantId, WebhookEndpointId endpointId, EventId eventId, CancellationToken cancellationToken = default);

    void Add(WebhookDelivery delivery);
}

/// <summary>Un travail du job pour un tenant : (tenant, livraison).</summary>
public sealed record DeliveryWork(TenantId TenantId, DeliveryId DeliveryId);

/// <summary>Pour le job SEULEMENT : tous les tenants.</summary>
public interface IWebhookWorkQueue
{
    /// <summary>Livraisons dues : en attente ou en échec, échéance passée, sans tentative en cours.</summary>
    Task<IReadOnlyList<DeliveryWork>> ListDueAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Tentatives commencées avant <paramref name="startedBeforeUtc"/> et jamais closes (worker perdu).</summary>
    Task<IReadOnlyList<DeliveryWork>> ListStuckAsync(DateTimeOffset startedBeforeUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Efface les livraisons TERMINÉES (réussies ou abandonnées) créées avant la date ; renvoie leur nombre.</summary>
    Task<int> PurgeFinishedAsync(DateTimeOffset createdBeforeUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Endpoints dont l'ancien secret (période de grâce) a expiré : à oublier et à supprimer du coffre.</summary>
    Task<IReadOnlyList<EndpointWork>> ListExpiredPreviousSecretsAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>Un travail du job sur un endpoint : (tenant, endpoint).</summary>
public sealed record EndpointWork(TenantId TenantId, WebhookEndpointId EndpointId);

public interface IApiCredentialRepository
{
    Task<ApiCredential?> FindAsync(TenantId tenantId, CredentialId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, des plus récents aux plus anciens.</summary>
    Task<IReadOnlyList<ApiCredential>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(ApiCredential credential);
}

/// <summary>
/// Authentification par clé : retrouve un credential par l'empreinte de la clé présentée, TOUS tenants (la requête n'a pas
/// encore de tenant : c'est le credential qui le donne). Lecture seule.
/// </summary>
public interface IApiCredentialLookup
{
    Task<ApiCredential?> FindByHashAsync(ApiKeyHash keyHash, CancellationToken cancellationToken = default);
}

/// <summary>
/// Horodatage d'usage d'une clé, à l'authentification : écriture directe et bornée (au plus une par
/// <c>KeyUsageResolution</c>), hors unité de travail, sans événement. Une erreur ici ne doit jamais refuser la requête.
/// </summary>
public interface IApiCredentialUsage
{
    /// <summary>Pose <paramref name="nowUtc"/> si la dernière utilisation enregistrée est absente ou antérieure à <paramref name="notSinceUtc"/>.</summary>
    Task RecordUseAsync(CredentialId credentialId, DateTimeOffset nowUtc, DateTimeOffset notSinceUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// L'auteur de la requête en cours, pour l'audit : l'utilisateur de l'access token, ou la clé d'API ; null hors requête
/// (job, outbox : le système). Fourni par la couche Api.
/// </summary>
public interface IIntegrationsActor
{
    ChangeActor? Current { get; }
}

/// <summary>
/// Le coffre des secrets de signature des webhooks. Le secret en clair n'existe que le temps de signer (ou de l'afficher
/// UNE fois à sa création) ; il n'est jamais journalisé ni renvoyé ensuite. Écritures dans la transaction de l'unit of work.
/// </summary>
public interface IWebhookSecretVault
{
    /// <summary>Crée un secret pour le tenant ; renvoie sa référence et sa valeur, à montrer une seule fois.</summary>
    (SecretReference Reference, string Secret) Create(TenantId tenantId);

    /// <summary>La valeur d'un secret du tenant ; null si la référence est inconnue ou d'un autre tenant.</summary>
    Task<string?> RevealAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken = default);

    /// <summary>Supprime un secret remplacé (la rotation n'a pas de période de grâce).</summary>
    Task DeleteAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken = default);
}

/// <summary>Une requête de webhook prête à partir : corps exact et en-têtes signés.</summary>
public sealed record WebhookRequest(Uri Url, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>Issue d'un envoi : succès (2xx) ou raison courte et stable (<c>http_503</c>, <c>timeout</c>, <c>blocked_address</c>...).</summary>
public sealed record WebhookSendResult(bool Succeeded, string? FailureReason)
{
    public static WebhookSendResult Success { get; } = new(true, null);

    public static WebhookSendResult Failure(string reason) => new(false, reason);
}

/// <summary>
/// Le client HTTP des webhooks. Il DOIT refuser toute adresse non publique après résolution DNS (chaque adresse), ne pas
/// suivre les redirections, borner le temps et la taille de la réponse. Ne journalise ni le corps ni les en-têtes.
/// </summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Integrations doit savoir du tenant, fourni par l'hôte depuis Tenants.</summary>
public interface IIntegrationTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

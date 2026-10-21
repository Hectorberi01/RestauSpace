using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Integrations.Application.Webhooks;
using Integrations.Domain;

namespace Integrations.Application.Queries;

// Lectures du restaurant du contexte. Jamais exposés : un secret de signature (ni sa référence dans le coffre), une clé
// d'API ni son empreinte. Une ressource d'un autre restaurant répond « introuvable ».

/// <param name="Url">Forme masquée : schéma, hôte et port ; le chemin et la requête (jeton du destinataire) n'en sortent jamais.</param>
/// <param name="UrlFingerprint">Empreinte courte de l'URL complète : distingue deux endpoints d'un même hôte sans la révéler.</param>
/// <param name="PreviousSecretValidUntilUtc">Fin de la période de grâce de l'ancien secret (deux signatures jusque-là).</param>
/// <param name="ConsecutiveDeadLetters">Livraisons abandonnées d'affilée ; à <c>AutoDisableAfterDeadLetters</c>, désactivation automatique.</param>
public sealed record WebhookEndpointDto(
    Guid Id,
    string Url,
    string UrlFingerprint,
    IReadOnlyList<string> Events,
    WebhookEndpointStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SecretRotatedAtUtc,
    DateTimeOffset? PreviousSecretValidUntilUtc,
    int ConsecutiveDeadLetters,
    DateTimeOffset? AutoDisabledAtUtc)
{
    public static WebhookEndpointDto Of(WebhookEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new(
            endpoint.Id.Value,
            endpoint.Url.Redacted,
            Fingerprint(endpoint.Url.Value),
            endpoint.SubscribedEvents.Select(code => code.Value).OrderBy(code => code, StringComparer.Ordinal).ToList(),
            endpoint.Status,
            endpoint.CreatedAtUtc,
            endpoint.SecretRotatedAtUtc,
            endpoint.PreviousSecretExpiresAtUtc,
            endpoint.ConsecutiveDeadLetters,
            endpoint.AutoDisabledAtUtc);
    }

    /// <summary>12 premiers caractères hexadécimaux du SHA-256 de l'URL : stable, non réversible en pratique.</summary>
    public static string Fingerprint(string url) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..12].ToLowerInvariant();
}

/// <summary>Une livraison dans une liste : sans le corps.</summary>
public sealed record WebhookDeliverySummaryDto(
    Guid Id,
    Guid EndpointId,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastFailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SucceededAtUtc,
    DateTimeOffset? DeadLetteredAtUtc)
{
    public static WebhookDeliverySummaryDto Of(WebhookDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return new(
            delivery.Id.Value, delivery.EndpointId.Value, delivery.EventId.Value, delivery.Payload.EventType.Value, delivery.Status,
            delivery.AttemptCount, delivery.RetryPolicy.MaxAttempts, delivery.NextAttemptAtUtc, delivery.LastFailureReason,
            delivery.CreatedAtUtc, delivery.SucceededAtUtc, delivery.DeadLetteredAtUtc);
    }
}

/// <summary>Une livraison avec le corps envoyé (le même à chaque tentative), pour le débogage côté restaurant.</summary>
public sealed record WebhookDeliveryDto(WebhookDeliverySummaryDto Delivery, int SchemaVersion, string Body)
{
    public static WebhookDeliveryDto Of(WebhookDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return new(WebhookDeliverySummaryDto.Of(delivery), delivery.Payload.SchemaVersion, delivery.Payload.Payload);
    }
}

/// <param name="LastUsedAtUtc">Dernière authentification réussie, à l'heure près (null : jamais utilisée).</param>
/// <param name="IsDormant">Utilisable mais inutilisée depuis <c>DormantKeyAfter</c> : candidate à la révocation.</param>
public sealed record ApiCredentialDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Scopes,
    ApiCredentialStatus Status,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RotatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    bool IsDormant)
{
    public static ApiCredentialDto Of(ApiCredential credential, DateTimeOffset nowUtc, TimeSpan dormantAfter)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new(
            credential.Id.Value,
            credential.Name,
            credential.Scopes.Select(scope => scope.Value).OrderBy(scope => scope, StringComparer.Ordinal).ToList(),
            credential.Status,
            credential.IssuedAtUtc,
            credential.ExpiresAtUtc,
            credential.RotatedAtUtc,
            credential.RevokedAtUtc,
            credential.LastUsedAtUtc,
            credential.IsDormantAt(nowUtc, dormantAfter));
    }
}

/// <summary>Ce qu'un restaurant peut choisir : les événements publiés et les scopes qu'une clé peut recevoir.</summary>
public sealed record IntegrationsCatalogDto(int SchemaVersion, IReadOnlyList<string> EventTypes, IReadOnlyList<string> ApiScopes);

public sealed record ListWebhookEndpointsQuery : IQuery<IReadOnlyList<WebhookEndpointDto>>;

public sealed class ListWebhookEndpointsHandler(IntegrationsWorkspace workspace) : IQueryHandler<ListWebhookEndpointsQuery, IReadOnlyList<WebhookEndpointDto>>
{
    public async Task<IReadOnlyList<WebhookEndpointDto>> HandleAsync(ListWebhookEndpointsQuery query, CancellationToken cancellationToken = default) =>
        (await workspace.Endpoints.ListAsync(workspace.CurrentTenant, cancellationToken)).Select(WebhookEndpointDto.Of).ToList();
}

public sealed record GetWebhookEndpointQuery(Guid EndpointId) : IQuery<WebhookEndpointDto>;

public sealed class GetWebhookEndpointHandler(IntegrationsWorkspace workspace) : IQueryHandler<GetWebhookEndpointQuery, WebhookEndpointDto>
{
    public async Task<WebhookEndpointDto> HandleAsync(GetWebhookEndpointQuery query, CancellationToken cancellationToken = default) =>
        WebhookEndpointDto.Of(await workspace.LoadEndpointAsync(query.EndpointId, cancellationToken));
}

/// <summary>Les livraisons d'un endpoint du restaurant, des plus récentes aux plus anciennes, filtrables par statut.</summary>
public sealed record ListWebhookDeliveriesQuery(Guid EndpointId, WebhookDeliveryStatus? Status, int Skip, int Take)
    : IQuery<IReadOnlyList<WebhookDeliverySummaryDto>>;

public sealed class ListWebhookDeliveriesHandler(IntegrationsWorkspace workspace)
    : IQueryHandler<ListWebhookDeliveriesQuery, IReadOnlyList<WebhookDeliverySummaryDto>>
{
    public async Task<IReadOnlyList<WebhookDeliverySummaryDto>> HandleAsync(ListWebhookDeliveriesQuery query, CancellationToken cancellationToken = default)
    {
        IntegrationsWorkspace.EnsurePage(query.Skip, query.Take);
        var endpoint = await workspace.LoadEndpointAsync(query.EndpointId, cancellationToken);
        return (await workspace.Deliveries.ListAsync(workspace.CurrentTenant, endpoint.Id, query.Status, query.Skip, query.Take, cancellationToken))
            .Select(WebhookDeliverySummaryDto.Of)
            .ToList();
    }
}

public sealed record GetWebhookDeliveryQuery(Guid DeliveryId) : IQuery<WebhookDeliveryDto>;

public sealed class GetWebhookDeliveryHandler(IntegrationsWorkspace workspace) : IQueryHandler<GetWebhookDeliveryQuery, WebhookDeliveryDto>
{
    public async Task<WebhookDeliveryDto> HandleAsync(GetWebhookDeliveryQuery query, CancellationToken cancellationToken = default) =>
        WebhookDeliveryDto.Of(await workspace.LoadDeliveryAsync(query.DeliveryId, cancellationToken));
}

/// <param name="DormantOnly">Vrai : seulement les clés dormantes (à passer en revue, puis révoquer).</param>
public sealed record ListApiCredentialsQuery(bool DormantOnly = false) : IQuery<IReadOnlyList<ApiCredentialDto>>;

public sealed class ListApiCredentialsHandler(IntegrationsWorkspace workspace, IClock clock) : IQueryHandler<ListApiCredentialsQuery, IReadOnlyList<ApiCredentialDto>>
{
    public async Task<IReadOnlyList<ApiCredentialDto>> HandleAsync(ListApiCredentialsQuery query, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return (await workspace.Credentials.ListAsync(workspace.CurrentTenant, cancellationToken))
            .Select(credential => ApiCredentialDto.Of(credential, now, workspace.Settings.DormantKeyAfter))
            .Where(credential => !query.DormantOnly || credential.IsDormant)
            .ToList();
    }
}

public sealed record GetApiCredentialQuery(Guid CredentialId) : IQuery<ApiCredentialDto>;

public sealed class GetApiCredentialHandler(IntegrationsWorkspace workspace, IClock clock) : IQueryHandler<GetApiCredentialQuery, ApiCredentialDto>
{
    public async Task<ApiCredentialDto> HandleAsync(GetApiCredentialQuery query, CancellationToken cancellationToken = default) =>
        ApiCredentialDto.Of(await workspace.LoadCredentialAsync(query.CredentialId, cancellationToken), clock.UtcNow, workspace.Settings.DormantKeyAfter);
}

public sealed record GetIntegrationsCatalogQuery : IQuery<IntegrationsCatalogDto>;

public sealed class GetIntegrationsCatalogHandler(IntegrationsSettings settings) : IQueryHandler<GetIntegrationsCatalogQuery, IntegrationsCatalogDto>
{
    public Task<IntegrationsCatalogDto> HandleAsync(GetIntegrationsCatalogQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new IntegrationsCatalogDto(
            WebhookEvents.SchemaVersion,
            WebhookEvents.All,
            settings.ApiScopes.Distinct(StringComparer.Ordinal).OrderBy(scope => scope, StringComparer.Ordinal).ToList()));
}

/// <summary>
/// Qui présente cette clé : le restaurant (du credential, jamais de la requête), l'id du credential et ses scopes
/// ENCORE au catalogue (un scope retiré du catalogue cesse de valoir pour les clés déjà émises).
/// </summary>
public sealed record ApiKeyPrincipal(Guid TenantId, Guid CredentialId, IReadOnlyList<string> Scopes);

/// <summary>
/// Authentification par clé d'API : null si la clé est inconnue, révoquée ou expirée (une seule réponse, sans détail).
/// Aucune trace de la clé : ni journal ni exception.
/// </summary>
public sealed record AuthenticateApiKeyQuery(string? Key) : IQuery<ApiKeyPrincipal?>;

public sealed class AuthenticateApiKeyHandler(IApiCredentialLookup lookup, IApiCredentialUsage usage, IntegrationsSettings settings, IClock clock)
    : IQueryHandler<AuthenticateApiKeyQuery, ApiKeyPrincipal?>
{
    public async Task<ApiKeyPrincipal?> HandleAsync(AuthenticateApiKeyQuery query, CancellationToken cancellationToken = default)
    {
        if (!ApiKeys.LooksValid(query.Key))
        {
            return null;
        }

        var now = clock.UtcNow;
        var credential = await lookup.FindByHashAsync(ApiKeys.Hash(query.Key!), cancellationToken);
        if (credential is null || !credential.IsUsableAt(now))
        {
            return null;
        }

        // Date de dernière utilisation, à l'heure près : une écriture au plus par clé et par période.
        var notSince = now - settings.KeyUsageResolution;
        if (credential.LastUsedAtUtc is null || credential.LastUsedAtUtc <= notSince)
        {
            await usage.RecordUseAsync(credential.Id, now, notSince, cancellationToken);
        }

        var catalog = new HashSet<string>(settings.ApiScopes, StringComparer.Ordinal);
        return new ApiKeyPrincipal(
            credential.TenantId.Value,
            credential.Id.Value,
            credential.Scopes.Select(scope => scope.Value).Where(catalog.Contains).OrderBy(scope => scope, StringComparer.Ordinal).ToList());
    }
}

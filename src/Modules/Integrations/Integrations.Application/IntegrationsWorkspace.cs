using Integrations.Application.Abstractions;
using Integrations.Domain;

namespace Integrations.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Le tenant vient du contexte (requête ou job), jamais d'une valeur de requête ; une
/// écriture du restaurant exige un tenant qui les accepte. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class IntegrationsWorkspace(
    ITenantContext tenantContext,
    IIntegrationTenants tenants,
    IWebhookEndpointRepository endpoints,
    IWebhookDeliveryRepository deliveries,
    IApiCredentialRepository credentials,
    IWebhookSecretVault secrets,
    IntegrationsSettings settings,
    IIntegrationsUnitOfWork unitOfWork,
    IIntegrationsActor actor)
{
    public const int MaxTake = 100;

    public IWebhookEndpointRepository Endpoints => endpoints;

    public IWebhookDeliveryRepository Deliveries => deliveries;

    public IApiCredentialRepository Credentials => credentials;

    public IWebhookSecretVault Secrets => secrets;

    public IntegrationsSettings Settings => settings;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>L'auteur de la requête (utilisateur ou clé d'API), pour les événements d'audit ; null pour un job.</summary>
    public ChangeActor? Actor => actor.Current;

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(IntegrationsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<WebhookEndpoint> LoadEndpointAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await endpoints.FindAsync(CurrentTenant, WebhookEndpointId.From(id), cancellationToken))
        ?? throw new NotFoundException(IntegrationsApplicationErrors.EndpointNotFound);

    public async Task<WebhookDelivery> LoadDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await deliveries.FindAsync(CurrentTenant, DeliveryId.From(id), cancellationToken))
        ?? throw new NotFoundException(IntegrationsApplicationErrors.DeliveryNotFound);

    public async Task<ApiCredential> LoadCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await credentials.FindAsync(CurrentTenant, CredentialId.From(id), cancellationToken))
        ?? throw new NotFoundException(IntegrationsApplicationErrors.CredentialNotFound);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, IntegrationsApplicationErrors.PageInvalid);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

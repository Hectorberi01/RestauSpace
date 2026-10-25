using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Integrations.Application.Queries;
using Integrations.Application.Webhooks;
using Integrations.Domain;

namespace Integrations.Application;

/// <summary>
/// Ports en mémoire du module Integrations. Comme les vrais, ils ne répondent que pour le tenant donné (sauf la recherche
/// par empreinte, qui voit tous les tenants). Le tenant de « la requête » est <see cref="CurrentTenantId"/>. Le coffre
/// garde les secrets en clair ici (le chiffrement est testé avec l'Infrastructure) ; l'envoi est enregistré, et répond
/// <see cref="NextResult"/>.
/// </summary>
public sealed class IntegrationsFixture : ITenantContext, IIntegrationTenants, IWebhookEndpointRepository, IWebhookDeliveryRepository,
    IApiCredentialRepository, IApiCredentialLookup, IApiCredentialUsage, IWebhookSecretVault, IWebhookSender, IIntegrationsUnitOfWork,
    IIntegrationsActor, IPermissionChecker, IPlanFeatureGate
{
    public IntegrationsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
        Settings.ApiScopes.Add("orders.view");
        Settings.ApiScopes.Add("orders.manage");
        Settings.ApiScopes.Add("catalog.view");
        Settings.RetryPolicy = WebhookRetryPolicy.Create(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    /// <summary>Tenants sans l'option API (les autres l'ont).</summary>
    public HashSet<TenantId> WithoutApiOption { get; } = [];

    /// <summary>Fonctionnalités absentes du plan, par tenant (tout le reste est permis).</summary>
    public HashSet<(TenantId Tenant, string Feature)> NotInPlan { get; } = [];

    public List<WebhookEndpoint> Endpoints { get; } = [];

    public List<WebhookDelivery> Deliveries { get; } = [];

    public List<ApiCredential> Credentials { get; } = [];

    /// <summary>Le coffre : (tenant, référence) -> secret.</summary>
    public Dictionary<(TenantId, string), string> Secrets { get; } = [];

    public List<WebhookRequest> Sent { get; } = [];

    public WebhookSendResult NextResult { get; set; } = WebhookSendResult.Success;

    public int Saves { get; private set; }

    /// <summary>L'auteur des requêtes (null : un job).</summary>
    public ChangeActor? Actor { get; set; } = ChangeActor.ForUser(Guid.NewGuid());

    /// <summary>Dernières utilisations enregistrées, par credential.</summary>
    public Dictionary<CredentialId, DateTimeOffset> Usage { get; } = [];

    public IntegrationsSettings Settings { get; } = new();

    public FakeClock Clock { get; } = new();

    public IntegrationsWorkspace Workspace => new(this, this, this, this, this, this, Settings, this, this);

    public CreateWebhookEndpointHandler CreateEndpoint => new(Workspace, this, Clock);

    public SubscribeWebhookEventHandler Subscribe => new(Workspace);

    public UnsubscribeWebhookEventHandler Unsubscribe => new(Workspace);

    public SetWebhookEndpointEnabledHandler SetEnabled => new(Workspace, Clock);

    public RotateWebhookSecretHandler RotateSecret => new(Workspace, Clock);

    public PublishWebhookEventHandler Publish => new(Workspace, this, Clock);

    public EnsureApiKeyPlanHandler EnsureApiKeyPlan => new(this, this);

    public AttemptWebhookDeliveryHandler Attempt => new(Workspace, this, Clock);

    public RecoverStuckDeliveryHandler RecoverStuck => new(Workspace, Clock);

    public RetryDeadLetteredDeliveryHandler RetryDeadLettered => new(Workspace, Clock);

    public IssueApiCredentialHandler Issue => new(Workspace, this, Clock);

    public RotateApiCredentialHandler RotateKey => new(Workspace, this, Clock);

    public RevokeApiCredentialHandler Revoke => new(Workspace, Clock);

    public AuthenticateApiKeyHandler Authenticate => new(this, this, Settings, Clock);

    public ExpirePreviousWebhookSecretHandler ExpirePrevious => new(Workspace, Clock);

    public SendWebhookTestHandler SendTest => new(Workspace, this, Clock);

    public ListApiCredentialsHandler ListCredentials => new(Workspace, Clock);

    public ListWebhookEndpointsHandler ListEndpoints => new(Workspace);

    public GetWebhookEndpointHandler GetEndpoint => new(Workspace);

    public ListWebhookDeliveriesHandler ListDeliveries => new(Workspace);

    public GetWebhookDeliveryHandler GetDelivery => new(Workspace);

    public GetApiCredentialHandler GetCredential => new(Workspace, Clock);

    /// <summary>Un endpoint du restaurant courant abonné à <paramref name="events"/> ; renvoie l'id et le secret.</summary>
    public Task<WebhookSecretIssued> NewEndpointAsync(params string[] events) =>
        CreateEndpoint.HandleAsync(new CreateWebhookEndpointCommand(
            "https://hooks.example.com/saas", events.Length == 0 ? new[] { WebhookEvents.OrderPlaced } : events));

    /// <summary>Publie un événement du restaurant courant (données minimales).</summary>
    public Task<int> PublishAsync(string eventType, Guid? eventId = null) =>
        Publish.HandleAsync(new PublishWebhookEventCommand(eventType, eventId ?? Guid.NewGuid(), Clock.UtcNow, """{"order_id":"o-1"}"""));

    public Task<bool> AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken = default) =>
        Task.FromResult(!NotInPlan.Contains((tenantId, feature)));

    public WebhookEndpoint EndpointById(Guid id) => Endpoints.Single(endpoint => endpoint.Id.Value == id);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    ChangeActor? IIntegrationsActor.Current => Actor;

    /// <summary>Permissions refusées à l'utilisateur auteur (toutes les autres sont accordées).</summary>
    public HashSet<string> DeniedPermissions { get; } = new(StringComparer.Ordinal);

    Task<bool> IPermissionChecker.HasTenantPermissionAsync(UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken) =>
        Task.FromResult(!DeniedPermissions.Contains(permission));

    Task<bool> IPermissionChecker.HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken) =>
        Task.FromResult(!DeniedPermissions.Contains(permission));

    Task IApiCredentialUsage.RecordUseAsync(CredentialId credentialId, DateTimeOffset nowUtc, DateTimeOffset notSinceUtc, CancellationToken cancellationToken)
    {
        if (!Usage.TryGetValue(credentialId, out var last) || last <= notSinceUtc)
        {
            Usage[credentialId] = nowUtc;
        }

        return Task.CompletedTask;
    }

    Task<bool> IIntegrationTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<bool> IIntegrationTenants.AllowsApiKeysAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(!WithoutApiOption.Contains(tenantId));

    Task<WebhookEndpoint?> IWebhookEndpointRepository.FindAsync(TenantId tenantId, WebhookEndpointId id, CancellationToken cancellationToken) =>
        Task.FromResult<WebhookEndpoint?>(Endpoints.SingleOrDefault(endpoint => endpoint.TenantId == tenantId && endpoint.Id == id));

    Task<IReadOnlyList<WebhookEndpoint>> IWebhookEndpointRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookEndpoint>>(Endpoints.Where(endpoint => endpoint.TenantId == tenantId).ToList());

    Task<IReadOnlyList<WebhookEndpoint>> IWebhookEndpointRepository.ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookEndpoint>>(Endpoints
            .Where(endpoint => endpoint.TenantId == tenantId && endpoint.Status == WebhookEndpointStatus.Active)
            .ToList());

    void IWebhookEndpointRepository.Add(WebhookEndpoint endpoint) => Endpoints.Add(endpoint);

    Task<WebhookDelivery?> IWebhookDeliveryRepository.FindAsync(TenantId tenantId, DeliveryId id, CancellationToken cancellationToken) =>
        Task.FromResult<WebhookDelivery?>(Deliveries.SingleOrDefault(delivery => delivery.TenantId == tenantId && delivery.Id == id));

    Task<IReadOnlyList<WebhookDelivery>> IWebhookDeliveryRepository.ListAsync(
        TenantId tenantId, WebhookEndpointId endpointId, WebhookDeliveryStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookDelivery>>(Deliveries
            .Where(delivery => delivery.TenantId == tenantId && delivery.EndpointId == endpointId && (status is null || delivery.Status == status))
            .OrderByDescending(delivery => delivery.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<bool> IWebhookDeliveryRepository.HasLiveDeliveryAsync(TenantId tenantId, WebhookEndpointId endpointId, EventId eventId, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(delivery => delivery.TenantId == tenantId && delivery.EndpointId == endpointId
            && delivery.EventId == eventId && delivery.Status != WebhookDeliveryStatus.DeadLetter));

    void IWebhookDeliveryRepository.Add(WebhookDelivery delivery) => Deliveries.Add(delivery);

    Task<ApiCredential?> IApiCredentialRepository.FindAsync(TenantId tenantId, CredentialId id, CancellationToken cancellationToken) =>
        Task.FromResult<ApiCredential?>(Credentials.SingleOrDefault(credential => credential.TenantId == tenantId && credential.Id == id));

    Task<IReadOnlyList<ApiCredential>> IApiCredentialRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApiCredential>>(Credentials.Where(credential => credential.TenantId == tenantId).ToList());

    void IApiCredentialRepository.Add(ApiCredential credential) => Credentials.Add(credential);

    Task<ApiCredential?> IApiCredentialLookup.FindByHashAsync(ApiKeyHash keyHash, CancellationToken cancellationToken) =>
        Task.FromResult<ApiCredential?>(Credentials.SingleOrDefault(credential => credential.KeyHash == keyHash));

    (SecretReference Reference, string Secret) IWebhookSecretVault.Create(TenantId tenantId)
    {
        var reference = SecretReference.Create($"test/{Guid.NewGuid():N}");
        var secret = WebhookSignature.NewSecret();
        Secrets[(tenantId, reference.Value)] = secret;
        return (reference, secret);
    }

    Task<string?> IWebhookSecretVault.RevealAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Secrets.GetValueOrDefault((tenantId, reference.Value)));

    Task IWebhookSecretVault.DeleteAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken)
    {
        Secrets.Remove((tenantId, reference.Value));
        return Task.CompletedTask;
    }

    Task<WebhookSendResult> IWebhookSender.SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        Sent.Add(request);
        return Task.FromResult(NextResult);
    }
}

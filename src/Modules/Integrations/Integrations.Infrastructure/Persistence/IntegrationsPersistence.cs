using Integrations.Application;
using Integrations.Application.Abstractions;
using Integrations.Domain;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using EventId = Integrations.Domain.EventId;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Integrations.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte. Les seules lectures sans filtre de
// tenant (IgnoreQueryFilters) sont celles du job (WebhookWorkQueue) et de l'authentification par clé
// (ApiCredentialLookup), et chacune le déclare.

internal sealed class WebhookEndpointRepository(IntegrationsDbContext context) : IWebhookEndpointRepository
{
    public Task<WebhookEndpoint?> FindAsync(TenantId tenantId, WebhookEndpointId id, CancellationToken cancellationToken = default) =>
        context.Endpoints.SingleOrDefaultAsync(endpoint => endpoint.TenantId == tenantId && endpoint.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.TenantId == tenantId)
            .OrderBy(endpoint => endpoint.CreatedAtUtc)
            .ThenBy(endpoint => endpoint.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebhookEndpoint>> ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.TenantId == tenantId && endpoint.Status == WebhookEndpointStatus.Active)
            .OrderBy(endpoint => endpoint.CreatedAtUtc)
            .ThenBy(endpoint => endpoint.Id)
            .ToListAsync(cancellationToken);

    public void Add(WebhookEndpoint endpoint) => context.Endpoints.Add(endpoint);
}

internal sealed class WebhookDeliveryRepository(IntegrationsDbContext context) : IWebhookDeliveryRepository
{
    public Task<WebhookDelivery?> FindAsync(TenantId tenantId, DeliveryId id, CancellationToken cancellationToken = default) =>
        context.Deliveries.SingleOrDefaultAsync(delivery => delivery.TenantId == tenantId && delivery.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WebhookDelivery>> ListAsync(
        TenantId tenantId, WebhookEndpointId endpointId, WebhookDeliveryStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var deliveries = context.Deliveries.AsNoTracking().Where(delivery => delivery.TenantId == tenantId && delivery.EndpointId == endpointId);
        if (status is { } wanted)
        {
            deliveries = deliveries.Where(delivery => delivery.Status == wanted);
        }

        return await deliveries
            .OrderByDescending(delivery => delivery.CreatedAtUtc)
            .ThenBy(delivery => delivery.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasLiveDeliveryAsync(TenantId tenantId, WebhookEndpointId endpointId, EventId eventId, CancellationToken cancellationToken = default)
    {
        // D'abord les livraisons ajoutées dans cette unité de travail, pas encore écrites.
        if (context.Deliveries.Local.Any(delivery => delivery.TenantId == tenantId && delivery.EndpointId == endpointId
            && delivery.EventId == eventId && delivery.Status != WebhookDeliveryStatus.DeadLetter))
        {
            return true;
        }

        return await context.Deliveries.AsNoTracking().AnyAsync(
            delivery => delivery.TenantId == tenantId && delivery.EndpointId == endpointId
                && delivery.EventId == eventId && delivery.Status != WebhookDeliveryStatus.DeadLetter,
            cancellationToken);
    }

    public void Add(WebhookDelivery delivery) => context.Deliveries.Add(delivery);
}

internal sealed class ApiCredentialRepository(IntegrationsDbContext context) : IApiCredentialRepository
{
    public Task<ApiCredential?> FindAsync(TenantId tenantId, CredentialId id, CancellationToken cancellationToken = default) =>
        context.Credentials.SingleOrDefaultAsync(credential => credential.TenantId == tenantId && credential.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiCredential>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Credentials.AsNoTracking()
            .Where(credential => credential.TenantId == tenantId)
            .OrderByDescending(credential => credential.IssuedAtUtc)
            .ThenBy(credential => credential.Id)
            .ToListAsync(cancellationToken);

    public void Add(ApiCredential credential) => context.Credentials.Add(credential);
}

/// <summary>
/// Authentification par clé : la requête n'a pas encore de tenant, c'est le credential qui le donne. Tous tenants,
/// déclaré (IgnoreQueryFilters), lecture seule, par l'empreinte (index unique).
/// </summary>
internal sealed class ApiCredentialLookup(IntegrationsDbContext context) : IApiCredentialLookup
{
    public Task<ApiCredential?> FindByHashAsync(ApiKeyHash keyHash, CancellationToken cancellationToken = default) =>
        context.Credentials.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(credential => credential.KeyHash == keyHash, cancellationToken);
}

/// <summary>
/// Date de dernière utilisation d'une clé : UPDATE direct, borné par la condition (au plus une écriture par période),
/// tous tenants (la requête n'a pas encore de tenant), hors unité de travail et sans événement. Un échec est journalisé
/// et ignoré : il ne doit jamais refuser une requête authentifiée.
/// </summary>
internal sealed class ApiCredentialUsage(IntegrationsDbContext context, ILogger<ApiCredentialUsage> logger) : IApiCredentialUsage
{
    public async Task RecordUseAsync(CredentialId credentialId, DateTimeOffset nowUtc, DateTimeOffset notSinceUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Credentials.IgnoreQueryFilters()
                .Where(credential => credential.Id == credentialId
                    && (credential.LastUsedAtUtc == null || credential.LastUsedAtUtc <= notSinceUtc))
                .ExecuteUpdateAsync(update => update.SetProperty(credential => credential.LastUsedAtUtc, nowUtc), cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException)
        {
            logger.LogWarning("Last use of API credential {CredentialId} not recorded: {Error}.", credentialId.Value, exception.GetType().Name);
        }
    }
}

/// <summary>Sans couche Api (job, tests) : pas d'auteur connu, le système agit.</summary>
internal sealed class NoIntegrationsActor : IIntegrationsActor
{
    public ChangeActor? Current => null;
}

/// <summary>Les files du job : tous tenants, déclaré (IgnoreQueryFilters). Ne renvoie que des (tenant, id).</summary>
internal sealed class WebhookWorkQueue(IntegrationsDbContext context) : IWebhookWorkQueue
{
    public async Task<IReadOnlyList<DeliveryWork>> ListDueAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Deliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(delivery => (delivery.Status == WebhookDeliveryStatus.Pending || delivery.Status == WebhookDeliveryStatus.Failed)
                && delivery.CurrentAttemptStartedAtUtc == null
                && delivery.NextAttemptAtUtc <= nowUtc)
            .OrderBy(delivery => delivery.NextAttemptAtUtc)
            .Select(delivery => new { delivery.TenantId, delivery.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new DeliveryWork(row.TenantId, row.Id))
        .ToList();

    public async Task<IReadOnlyList<DeliveryWork>> ListStuckAsync(DateTimeOffset startedBeforeUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Deliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(delivery => delivery.CurrentAttemptStartedAtUtc != null && delivery.CurrentAttemptStartedAtUtc <= startedBeforeUtc)
            .OrderBy(delivery => delivery.CurrentAttemptStartedAtUtc)
            .Select(delivery => new { delivery.TenantId, delivery.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new DeliveryWork(row.TenantId, row.Id))
        .ToList();

    public async Task<int> PurgeFinishedAsync(DateTimeOffset createdBeforeUtc, int take, CancellationToken cancellationToken = default)
    {
        var ids = await context.Deliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(delivery => (delivery.Status == WebhookDeliveryStatus.Succeeded || delivery.Status == WebhookDeliveryStatus.DeadLetter)
                && delivery.CreatedAtUtc <= createdBeforeUtc)
            .OrderBy(delivery => delivery.CreatedAtUtc)
            .Select(delivery => delivery.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        // Le statut est revérifié : une livraison terminée ne change plus, mais la garde ne coûte rien.
        return await context.Deliveries.IgnoreQueryFilters()
            .Where(delivery => ids.Contains(delivery.Id)
                && (delivery.Status == WebhookDeliveryStatus.Succeeded || delivery.Status == WebhookDeliveryStatus.DeadLetter))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EndpointWork>> ListExpiredPreviousSecretsAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default) =>
        (await context.Endpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(endpoint => endpoint.PreviousSecretExpiresAtUtc != null && endpoint.PreviousSecretExpiresAtUtc <= nowUtc)
            .OrderBy(endpoint => endpoint.PreviousSecretExpiresAtUtc)
            .Select(endpoint => new { endpoint.TenantId, endpoint.Id })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new EndpointWork(row.TenantId, row.Id))
        .ToList();
}

internal sealed class IntegrationsUnitOfWork(IntegrationsDbContext context) : OutboxUnitOfWork<IntegrationsDbContext>(context), IIntegrationsUnitOfWork
{
    protected override DomainError ConcurrentUpdate => IntegrationsApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Deux livraisons vivantes du même événement vers le même endpoint (deux publications simultanées), ou empreinte de
    /// clé en double : la seconde écriture perd, comme un conflit de version.
    /// </summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: WebhookDeliveryConfiguration.LiveDeliveryIndex or ApiCredentialConfiguration.KeyHashIndex,
        }
            ? new ConcurrencyException(IntegrationsApplicationErrors.ConcurrentUpdate)
            : null;
}

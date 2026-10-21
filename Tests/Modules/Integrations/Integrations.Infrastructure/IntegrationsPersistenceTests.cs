using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Integrations.Application.Webhooks;
using Integrations.Domain;
using Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Integrations.Infrastructure;

/// <summary>
/// Persistance sur un vrai PostgreSQL (Docker) : relecture par le domaine, isolation par tenant, unicité d'une livraison
/// vivante, files du job, recherche d'une clé par empreinte, coffre chiffré, outbox.
/// </summary>
public sealed class IntegrationsPersistenceTests(IntegrationsDatabase database) : IClassFixture<IntegrationsDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task<WebhookEndpoint> SavedEndpointAsync(TenantId tenant, params string[] events)
    {
        WebhookEndpoint endpoint = null!;
        await database.SaveAsync(tenant, services =>
        {
            var (reference, _) = services.GetRequiredService<IWebhookSecretVault>().Create(tenant);
            endpoint = WebhookEndpoint.Create(
                tenant,
                WebhookUrl.Create("https://hooks.example.com/in"),
                (events.Length == 0 ? new[] { WebhookEvents.OrderPlaced } : events).Select(EventTypeCode.Create),
                reference,
                Clock);
            services.GetRequiredService<IWebhookEndpointRepository>().Add(endpoint);
            return Task.CompletedTask;
        });
        return endpoint;
    }

    private static WebhookPayloadSnapshot Payload(Guid eventId) => WebhookPayloadSnapshot.Create(
        EventTypeCode.Create(WebhookEvents.OrderPlaced), 1, WebhookEnvelope.Build(eventId, WebhookEvents.OrderPlaced, FakeClock.DefaultNow, "{}"));

    private async Task<WebhookDelivery> SavedDeliveryAsync(TenantId tenant, WebhookEndpoint endpoint, EventId? eventId = null)
    {
        var id = eventId ?? EventId.New();
        var delivery = WebhookDelivery.CreateFor(endpoint, id, Payload(id.Value), Clock, WebhookRetryPolicy.Default);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IWebhookDeliveryRepository>().Add(delivery);
            return Task.CompletedTask;
        });
        return delivery;
    }

    [Fact]
    public async Task An_endpoint_is_read_back_through_the_domain_and_seen_only_by_its_restaurant()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant, WebhookEvents.OrderPlaced, WebhookEvents.PaymentCaptured);

        await using var own = database.NewScope(tenant);
        await using var other = database.NewScope(TenantId.New());
        var loaded = await own.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id);

        Assert.NotNull(loaded);
        Assert.Equal((endpoint.Url, endpoint.SecretReference, WebhookEndpointStatus.Active), (loaded.Url, loaded.SecretReference, loaded.Status));
        Assert.True(loaded.ShouldReceive(EventTypeCode.Create(WebhookEvents.PaymentCaptured)));
        Assert.Null(await other.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id));
    }

    [Fact]
    public async Task A_subscription_change_is_saved()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id))!
                .Subscribe(EventTypeCode.Create(WebhookEvents.KitchenTicketReady)));

        await using var scope = database.NewScope(tenant);
        var loaded = await scope.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id);

        Assert.True(loaded!.ShouldReceive(EventTypeCode.Create(WebhookEvents.KitchenTicketReady)));
        Assert.Equal(2, loaded.SubscribedEvents.Count);
    }

    [Fact]
    public async Task A_delivery_keeps_its_body_and_retry_policy_and_a_second_live_delivery_of_the_same_event_is_refused()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        var delivery = await SavedDeliveryAsync(tenant, endpoint);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>().FindAsync(tenant, delivery.Id);
            Assert.NotNull(loaded);
            Assert.Equal((delivery.Payload, delivery.RetryPolicy, delivery.EventId), (loaded.Payload, loaded.RetryPolicy, loaded.EventId));
            Assert.True(await scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>().HasLiveDeliveryAsync(tenant, endpoint.Id, delivery.EventId));
        }

        await Assert.ThrowsAsync<ConcurrencyException>(() => SavedDeliveryAsync(tenant, endpoint, delivery.EventId));
    }

    [Fact]
    public async Task An_abandoned_delivery_lets_the_same_event_be_delivered_again()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        var delivery = await SavedDeliveryAsync(tenant, endpoint);
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IWebhookDeliveryRepository>().FindAsync(tenant, delivery.Id))!.DeadLetter("operator", Clock));

        var again = await SavedDeliveryAsync(tenant, endpoint, delivery.EventId);

        Assert.Equal(delivery.EventId, again.EventId);
    }

    [Fact]
    public async Task The_job_sees_due_and_stuck_deliveries_of_every_restaurant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var due = await SavedDeliveryAsync(tenantA, await SavedEndpointAsync(tenantA));
        var stuck = await SavedDeliveryAsync(tenantB, await SavedEndpointAsync(tenantB));
        await database.SaveAsync(tenantB, async services =>
            (await services.GetRequiredService<IWebhookDeliveryRepository>().FindAsync(tenantB, stuck.Id))!.StartAttempt(Clock));

        await using var platform = database.NewScope(null);
        var queue = platform.ServiceProvider.GetRequiredService<IWebhookWorkQueue>();
        var dueList = await queue.ListDueAsync(Clock.UtcNow, 1000);
        var stuckList = await queue.ListStuckAsync(Clock.UtcNow, 1000);

        Assert.Contains(new DeliveryWork(tenantA, due.Id), dueList);
        Assert.DoesNotContain(new DeliveryWork(tenantB, stuck.Id), dueList);
        Assert.Contains(new DeliveryWork(tenantB, stuck.Id), stuckList);
    }

    [Fact]
    public async Task Finished_deliveries_older_than_the_retention_are_purged_and_live_ones_kept()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        var finished = await SavedDeliveryAsync(tenant, endpoint);
        var live = await SavedDeliveryAsync(tenant, endpoint);
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IWebhookDeliveryRepository>().FindAsync(tenant, finished.Id))!.DeadLetter("operator", Clock));

        await using (var platform = database.NewScope(null))
        {
            _ = await platform.ServiceProvider.GetRequiredService<IWebhookWorkQueue>().PurgeFinishedAsync(Clock.UtcNow, 10_000);
        }

        await using var scope = database.NewScope(tenant);
        var repository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();
        Assert.Null(await repository.FindAsync(tenant, finished.Id));
        Assert.NotNull(await repository.FindAsync(tenant, live.Id));
    }

    [Fact]
    public async Task A_key_is_found_by_its_hash_from_any_scope_and_its_scopes_are_read_back()
    {
        var tenant = TenantId.New();
        var key = ApiKeys.NewKey();
        var credential = ApiCredential.Issue(
            tenant, "POS", ApiKeys.Hash(key), [ApiScope.Create("orders.view")], ApiScopeCatalog.Of("orders.view", "orders.manage"), null, Clock);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IApiCredentialRepository>().Add(credential);
            return Task.CompletedTask;
        });

        await using var anonymous = database.NewScope(null);
        var found = await anonymous.ServiceProvider.GetRequiredService<IApiCredentialLookup>().FindByHashAsync(ApiKeys.Hash(key));

        Assert.NotNull(found);
        Assert.Equal((credential.Id, tenant), (found.Id, found.TenantId));
        Assert.Equal(["orders.view"], found.Scopes.Select(scope => scope.Value));
        Assert.Null(await anonymous.ServiceProvider.GetRequiredService<IApiCredentialLookup>().FindByHashAsync(ApiKeys.Hash(ApiKeys.NewKey())));
    }

    [Fact]
    public async Task A_secret_is_stored_encrypted_and_revealed_only_to_its_restaurant()
    {
        var tenant = TenantId.New();
        var other = TenantId.New();
        SecretReference reference = null!;
        var secret = string.Empty;
        await database.SaveAsync(tenant, services =>
        {
            (reference, secret) = services.GetRequiredService<IWebhookSecretVault>().Create(tenant);
            return Task.CompletedTask;
        });

        await using var own = database.NewScope(tenant);
        await using var foreign = database.NewScope(other);
        var stored = await own.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Secrets.AsNoTracking()
            .SingleAsync(row => row.Reference == reference.Value);

        Assert.StartsWith("v1:", stored.CipherText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, stored.CipherText, StringComparison.Ordinal);
        Assert.Equal(secret, await own.ServiceProvider.GetRequiredService<IWebhookSecretVault>().RevealAsync(tenant, reference));
        Assert.Null(await foreign.ServiceProvider.GetRequiredService<IWebhookSecretVault>().RevealAsync(other, reference));
        Assert.Null(await foreign.ServiceProvider.GetRequiredService<IWebhookSecretVault>().RevealAsync(tenant, reference));
    }

    [Fact]
    public async Task A_delivery_outcome_is_written_to_the_outbox_without_any_secret()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        var delivery = await SavedDeliveryAsync(tenant, endpoint);
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IWebhookDeliveryRepository>().FindAsync(tenant, delivery.Id))!.DeadLetter("operator", Clock));

        await using var scope = database.NewScope(null);
        // content est en jsonb : le filtre sur le texte se fait après lecture.
        var message = (await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().OutboxMessages.AsNoTracking()
            .Where(candidate => candidate.Type == nameof(WebhookDeadLettered)).ToListAsync())
            .Single(candidate => candidate.Content.Contains(delivery.Id.Value.ToString(), StringComparison.Ordinal));

        Assert.DoesNotContain("whsec_", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(endpoint.SecretReference.Value, message.Content, StringComparison.Ordinal);
    }

    // --- Ajouts : période de grâce, dernière utilisation, auteur dans l'outbox

    [Fact]
    public async Task A_previous_secret_in_grace_is_read_back_and_listed_for_the_job_once_expired()
    {
        var tenant = TenantId.New();
        var endpoint = await SavedEndpointAsync(tenant);
        await database.SaveAsync(tenant, async services =>
        {
            var (reference, _) = services.GetRequiredService<IWebhookSecretVault>().Create(tenant);
            (await services.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id))!
                .RotateSecretReference(reference, Clock, TimeSpan.FromHours(24));
        });

        await using var scope = database.NewScope(tenant);
        var loaded = await scope.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>().FindAsync(tenant, endpoint.Id);
        await using var platform = database.NewScope(null);
        var queue = platform.ServiceProvider.GetRequiredService<IWebhookWorkQueue>();

        Assert.Equal(endpoint.SecretReference, loaded!.PreviousSecretReference);
        Assert.Equal(Clock.UtcNow.AddHours(24), loaded.PreviousSecretExpiresAtUtc);
        Assert.DoesNotContain(new EndpointWork(tenant, endpoint.Id), await queue.ListExpiredPreviousSecretsAsync(Clock.UtcNow, 1000));
        Assert.Contains(new EndpointWork(tenant, endpoint.Id), await queue.ListExpiredPreviousSecretsAsync(Clock.UtcNow.AddHours(24), 1000));
    }

    [Fact]
    public async Task A_key_use_is_written_once_per_period_without_a_tenant_in_scope()
    {
        var tenant = TenantId.New();
        var credential = ApiCredential.Issue(
            tenant, "POS", ApiKeys.Hash(ApiKeys.NewKey()), [ApiScope.Create("orders.view")], ApiScopeCatalog.Of("orders.view"), null, Clock);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IApiCredentialRepository>().Add(credential);
            return Task.CompletedTask;
        });
        var first = Clock.UtcNow;

        await using (var anonymous = database.NewScope(null))
        {
            var usage = anonymous.ServiceProvider.GetRequiredService<IApiCredentialUsage>();
            await usage.RecordUseAsync(credential.Id, first, first.AddHours(-1));
            await usage.RecordUseAsync(credential.Id, first.AddMinutes(10), first.AddMinutes(-50));
        }

        await using var scope = database.NewScope(tenant);
        var loaded = await scope.ServiceProvider.GetRequiredService<IApiCredentialRepository>().FindAsync(tenant, credential.Id);
        Assert.Equal(first, loaded!.LastUsedAtUtc);
    }

    [Fact]
    public async Task The_author_of_a_key_issue_reaches_the_outbox_without_the_hash()
    {
        var tenant = TenantId.New();
        var author = ChangeActor.ForUser(Guid.NewGuid());
        var hash = ApiKeys.Hash(ApiKeys.NewKey());
        var credential = ApiCredential.Issue(
            tenant, "POS", hash, [ApiScope.Create("orders.view")], ApiScopeCatalog.Of("orders.view"), null, Clock, author);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IApiCredentialRepository>().Add(credential);
            return Task.CompletedTask;
        });

        await using var scope = database.NewScope(null);
        // content est en jsonb : le filtre sur le texte se fait après lecture.
        var message = (await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().OutboxMessages.AsNoTracking()
            .Where(candidate => candidate.Type == nameof(ApiCredentialIssued)).ToListAsync())
            .Single(candidate => candidate.Content.Contains(credential.Id.Value.ToString(), StringComparison.Ordinal));

        Assert.Contains(author.Id!.Value.ToString(), message.Content, StringComparison.Ordinal);
        Assert.Contains("orders.view", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(hash.Value, message.Content, StringComparison.Ordinal);
    }
}

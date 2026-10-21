using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Integrations.Application.Abstractions;
using Integrations.Application.Commands;
using Integrations.Application.Queries;
using Integrations.Application.Webhooks;
using Integrations.Domain;

namespace Integrations.Application;

public sealed class IntegrationsUseCaseTests
{
    private readonly IntegrationsFixture _fixture = new();

    // --- Endpoints

    [Fact]
    public async Task An_endpoint_is_created_with_a_secret_shown_once_and_stored_only_as_a_reference()
    {
        var issued = await _fixture.NewEndpointAsync(WebhookEvents.OrderPlaced, WebhookEvents.PaymentCaptured);

        var endpoint = _fixture.EndpointById(issued.EndpointId);
        Assert.StartsWith(WebhookSignature.SecretPrefix, issued.Secret, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Secret, endpoint.SecretReference.Value, StringComparison.Ordinal);
        Assert.Equal(issued.Secret, _fixture.Secrets[(_fixture.TenantA, endpoint.SecretReference.Value)]);
        Assert.Equal(_fixture.TenantA, endpoint.TenantId);
        var dto = await _fixture.GetEndpoint.HandleAsync(new GetWebhookEndpointQuery(issued.EndpointId));
        Assert.Equal([WebhookEvents.OrderPlaced, WebhookEvents.PaymentCaptured], dto.Events);
    }

    [Fact]
    public async Task An_endpoint_only_subscribes_to_published_events_and_a_suspended_restaurant_creates_none()
    {
        var unknown = await Assert.ThrowsAsync<DomainException>(() => _fixture.NewEndpointAsync("orders.order_secret_stuff"));
        var internalUrl = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateEndpoint.HandleAsync(
            new CreateWebhookEndpointCommand("https://169.254.169.254/latest", [WebhookEvents.OrderPlaced])));
        _fixture.Writable[_fixture.TenantA] = false;
        var suspended = await Assert.ThrowsAsync<DomainException>(() => _fixture.NewEndpointAsync());

        Assert.Equal(IntegrationsApplicationErrors.EventTypeNotPublished, unknown.Error);
        Assert.Equal(WebhookEndpointErrors.UrlHostNotPublic, internalUrl.Error);
        Assert.Equal(IntegrationsApplicationErrors.TenantNotWritable, suspended.Error);
        Assert.Empty(_fixture.Endpoints);
        Assert.Empty(_fixture.Secrets);
    }

    [Fact]
    public async Task Another_restaurant_never_sees_nor_changes_an_endpoint()
    {
        var issued = await _fixture.NewEndpointAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetEndpoint.HandleAsync(new GetWebhookEndpointQuery(issued.EndpointId)));
        var rotate = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId)));
        var listed = await _fixture.ListEndpoints.HandleAsync(new ListWebhookEndpointsQuery());

        Assert.Equal(IntegrationsApplicationErrors.EndpointNotFound, read.Error);
        Assert.Equal(IntegrationsApplicationErrors.EndpointNotFound, rotate.Error);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task An_immediate_rotation_destroys_the_previous_secret()
    {
        var issued = await _fixture.NewEndpointAsync();
        var previous = _fixture.EndpointById(issued.EndpointId).SecretReference;

        var rotated = await _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId, Immediate: true));

        Assert.NotEqual(issued.Secret, rotated.Secret);
        Assert.False(_fixture.Secrets.ContainsKey((_fixture.TenantA, previous.Value)));
        Assert.Single(_fixture.Secrets);
        Assert.Null(_fixture.EndpointById(issued.EndpointId).PreviousSecretReference);
    }

    [Fact]
    public async Task A_suspended_restaurant_can_still_disable_an_endpoint_but_not_enable_it()
    {
        var issued = await _fixture.NewEndpointAsync();
        _fixture.Writable[_fixture.TenantA] = false;

        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, false));
        var enable = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, true)));

        Assert.Equal(WebhookEndpointStatus.Disabled, _fixture.EndpointById(issued.EndpointId).Status);
        Assert.Equal(IntegrationsApplicationErrors.TenantNotWritable, enable.Error);
    }

    [Fact]
    public void Endpoint_and_credential_responses_carry_no_secret_reference_nor_key_hash()
    {
        string[] forbidden = ["Secret", "SecretReference", "KeyHash", "Key", "Hash"];

        Assert.DoesNotContain(typeof(WebhookEndpointDto).GetProperties(), property => forbidden.Contains(property.Name));
        Assert.DoesNotContain(typeof(ApiCredentialDto).GetProperties(), property => forbidden.Contains(property.Name));
    }

    // --- Publication

    [Fact]
    public async Task An_event_becomes_one_delivery_per_active_subscribed_endpoint_of_its_restaurant_only()
    {
        var subscribed = await _fixture.NewEndpointAsync(WebhookEvents.OrderPlaced);
        _ = await _fixture.NewEndpointAsync(WebhookEvents.PaymentCaptured);
        var disabled = await _fixture.NewEndpointAsync(WebhookEvents.OrderPlaced);
        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(disabled.EndpointId, false));
        _fixture.CurrentTenantId = _fixture.TenantB;
        _ = await _fixture.NewEndpointAsync(WebhookEvents.OrderPlaced);
        _fixture.CurrentTenantId = _fixture.TenantA;

        var created = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);

        Assert.Equal(1, created);
        var delivery = Assert.Single(_fixture.Deliveries);
        Assert.Equal((subscribed.EndpointId, _fixture.TenantA), (delivery.EndpointId.Value, delivery.TenantId));
        using var body = JsonDocument.Parse(delivery.Payload.Payload);
        Assert.Equal(delivery.EventId.Value, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(WebhookEvents.OrderPlaced, body.RootElement.GetProperty("type").GetString());
        Assert.Equal("o-1", body.RootElement.GetProperty("data").GetProperty("order_id").GetString());
    }

    [Fact]
    public async Task A_replayed_event_creates_no_second_delivery()
    {
        _ = await _fixture.NewEndpointAsync();
        var eventId = Guid.NewGuid();

        var first = await _fixture.PublishAsync(WebhookEvents.OrderPlaced, eventId);
        var replay = await _fixture.PublishAsync(WebhookEvents.OrderPlaced, eventId);

        Assert.Equal((1, 0), (first, replay));
        Assert.Single(_fixture.Deliveries);
    }

    // --- Envoi

    [Fact]
    public async Task A_due_delivery_is_sent_signed_with_the_endpoint_secret_and_succeeds()
    {
        var issued = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        var delivery = Assert.Single(_fixture.Deliveries);

        var outcome = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));

        Assert.Equal(AttemptOutcome.Delivered, outcome);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        var request = Assert.Single(_fixture.Sent);
        Assert.Equal(new Uri("https://hooks.example.com/saas"), request.Url);
        Assert.Equal(delivery.Payload.Payload, request.Body);
        Assert.Equal(delivery.EventId.Value.ToString("D"), request.Headers[WebhookSignature.IdHeader]);
        var signed = $"{request.Headers[WebhookSignature.IdHeader]}.{request.Headers[WebhookSignature.TimestampHeader]}.{request.Body}";
        var expected = "v1," + Convert.ToBase64String(HMACSHA256.HashData(
            Convert.FromBase64String(issued.Secret[WebhookSignature.SecretPrefix.Length..]), Encoding.UTF8.GetBytes(signed)));
        Assert.Equal(expected, request.Headers[WebhookSignature.SignatureHeader]);
    }

    [Fact]
    public async Task A_refused_delivery_is_retried_later_then_abandoned_after_the_last_attempt()
    {
        _ = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        var delivery = Assert.Single(_fixture.Deliveries);
        _fixture.NextResult = WebhookSendResult.Failure("http_503");

        var first = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));
        var tooEarly = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));
        _fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var second = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var third = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));

        Assert.Equal(
            (AttemptOutcome.Failed, AttemptOutcome.Nothing, AttemptOutcome.Failed, AttemptOutcome.DeadLettered),
            (first, tooEarly, second, third));
        Assert.Equal((WebhookDeliveryStatus.DeadLetter, 3, "http_503"), (delivery.Status, delivery.AttemptCount, delivery.LastFailureReason));
        Assert.Equal(3, _fixture.Sent.Count);
    }

    [Fact]
    public async Task A_delivery_to_an_endpoint_disabled_since_is_abandoned_without_being_sent()
    {
        var issued = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, false));
        var delivery = Assert.Single(_fixture.Deliveries);

        var outcome = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));

        Assert.Equal(AttemptOutcome.DeadLettered, outcome);
        Assert.Equal(AttemptWebhookDeliveryHandler.EndpointDisabled, delivery.LastFailureReason);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task The_job_only_attempts_deliveries_of_its_scope_tenant()
    {
        _ = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        var delivery = Assert.Single(_fixture.Deliveries);
        _fixture.CurrentTenantId = _fixture.TenantB;

        var outcome = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value));

        Assert.Equal(AttemptOutcome.Nothing, outcome);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task A_lost_attempt_is_closed_as_timeout_only_once_it_is_old_enough()
    {
        _ = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        var delivery = Assert.Single(_fixture.Deliveries);
        delivery.StartAttempt(_fixture.Clock);

        var tooSoon = await _fixture.RecoverStuck.HandleAsync(new RecoverStuckDeliveryCommand(delivery.Id.Value));
        _fixture.Clock.Advance(_fixture.Settings.StuckAttemptAfter);
        var recovered = await _fixture.RecoverStuck.HandleAsync(new RecoverStuckDeliveryCommand(delivery.Id.Value));

        Assert.Equal((false, true), (tooSoon, recovered));
        Assert.Equal((WebhookDeliveryStatus.Failed, RecoverStuckDeliveryHandler.Timeout), (delivery.Status, delivery.LastFailureReason));
    }

    [Fact]
    public async Task An_abandoned_delivery_is_sent_again_as_a_new_delivery_of_the_same_event()
    {
        var issued = await _fixture.NewEndpointAsync();
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        var abandoned = Assert.Single(_fixture.Deliveries);
        var live = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.RetryDeadLettered.HandleAsync(new RetryDeadLetteredDeliveryCommand(abandoned.Id.Value)));
        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, false));
        _ = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(abandoned.Id.Value));
        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, true));

        var retryId = await _fixture.RetryDeadLettered.HandleAsync(new RetryDeadLetteredDeliveryCommand(abandoned.Id.Value));

        Assert.Equal(IntegrationsApplicationErrors.NotDeadLettered, live.Error);
        var retry = _fixture.Deliveries.Single(delivery => delivery.Id.Value == retryId);
        Assert.Equal((abandoned.EventId, abandoned.Payload.Payload, WebhookDeliveryStatus.Pending), (retry.EventId, retry.Payload.Payload, retry.Status));
    }

    // --- Clés d'API

    [Fact]
    public async Task A_key_is_shown_once_stored_as_a_hash_and_authenticates_for_its_own_restaurant_with_its_scopes()
    {
        var issued = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["orders.view"], null));

        var credential = Assert.Single(_fixture.Credentials);
        Assert.StartsWith(ApiKeys.Prefix, issued.Key, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Key, credential.KeyHash.Value, StringComparison.Ordinal);
        _fixture.CurrentTenantId = null;
        var principal = await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(issued.Key));
        Assert.NotNull(principal);
        Assert.Equal((_fixture.TenantA.Value, issued.CredentialId), (principal.TenantId, principal.CredentialId));
        Assert.Equal(["orders.view"], principal.Scopes);
    }

    [Fact]
    public async Task A_key_cannot_receive_a_scope_outside_the_catalog()
    {
        var staff = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["staff.manage"], null)));
        var integrations = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["integrations.manage"], null)));

        Assert.Equal(ApiCredentialErrors.ScopeUnknown, staff.Error);
        Assert.Equal(ApiCredentialErrors.ScopeUnknown, integrations.Error);
        Assert.Empty(_fixture.Credentials);
    }

    [Fact]
    public async Task A_revoked_expired_rotated_or_malformed_key_no_longer_authenticates()
    {
        var revoked = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("A", ["orders.view"], null));
        var expiring = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("B", ["orders.view"], _fixture.Clock.UtcNow.AddMinutes(5)));
        var rotatedFrom = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("C", ["orders.view"], null));
        await _fixture.Revoke.HandleAsync(new RevokeApiCredentialCommand(revoked.CredentialId));
        var rotatedTo = await _fixture.RotateKey.HandleAsync(new RotateApiCredentialCommand(rotatedFrom.CredentialId));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(revoked.Key)));
        Assert.Null(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(expiring.Key)));
        Assert.Null(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(rotatedFrom.Key)));
        Assert.NotNull(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(rotatedTo.Key)));
        Assert.Null(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery("srk_" + new string('x', 200))));
        Assert.Null(await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(null)));
    }

    [Fact]
    public async Task A_scope_removed_from_the_catalog_stops_counting_for_keys_already_issued()
    {
        var issued = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["orders.view", "catalog.view"], null));
        _fixture.Settings.ApiScopes.Remove("catalog.view");

        var principal = await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(issued.Key));

        Assert.Equal(["orders.view"], principal!.Scopes);
    }

    [Fact]
    public async Task Another_restaurant_cannot_revoke_a_key()
    {
        var issued = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["orders.view"], null));
        _fixture.CurrentTenantId = _fixture.TenantB;

        var revoke = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Revoke.HandleAsync(new RevokeApiCredentialCommand(issued.CredentialId)));

        Assert.Equal(IntegrationsApplicationErrors.CredentialNotFound, revoke.Error);
        Assert.Equal(ApiCredentialStatus.Active, Assert.Single(_fixture.Credentials).Status);
    }
}

public sealed class IntegrationsHardeningTests
{
    private readonly IntegrationsFixture _fixture = new();

    private static string Signature(string secret, WebhookRequest request) =>
        "v1," + Convert.ToBase64String(HMACSHA256.HashData(
            Convert.FromBase64String(secret[WebhookSignature.SecretPrefix.Length..]),
            Encoding.UTF8.GetBytes($"{request.Headers[WebhookSignature.IdHeader]}.{request.Headers[WebhookSignature.TimestampHeader]}.{request.Body}")));

    // --- Période de grâce du secret

    [Fact]
    public async Task During_the_grace_period_every_send_is_signed_with_the_new_and_the_previous_secret()
    {
        var issued = await _fixture.NewEndpointAsync();
        var rotated = await _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId));
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);

        _ = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(Assert.Single(_fixture.Deliveries).Id.Value));

        var request = Assert.Single(_fixture.Sent);
        Assert.Equal(
            $"{Signature(rotated.Secret, request)} {Signature(issued.Secret, request)}",
            request.Headers[WebhookSignature.SignatureHeader]);
        Assert.Equal(2, _fixture.Secrets.Count);
        Assert.Equal(_fixture.Clock.UtcNow + _fixture.Settings.SecretGracePeriod, _fixture.EndpointById(issued.EndpointId).PreviousSecretExpiresAtUtc);
    }

    [Fact]
    public async Task After_the_grace_period_the_previous_secret_is_forgotten_deleted_and_no_longer_signs()
    {
        var issued = await _fixture.NewEndpointAsync();
        var rotated = await _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId));

        var tooEarly = await _fixture.ExpirePrevious.HandleAsync(new ExpirePreviousWebhookSecretCommand(issued.EndpointId));
        _fixture.Clock.Advance(_fixture.Settings.SecretGracePeriod);
        var expired = await _fixture.ExpirePrevious.HandleAsync(new ExpirePreviousWebhookSecretCommand(issued.EndpointId));
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        _ = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(Assert.Single(_fixture.Deliveries).Id.Value));

        Assert.Equal((false, true), (tooEarly, expired));
        Assert.Single(_fixture.Secrets);
        var request = Assert.Single(_fixture.Sent);
        Assert.Equal(Signature(rotated.Secret, request), request.Headers[WebhookSignature.SignatureHeader]);
    }

    [Fact]
    public async Task A_second_rotation_during_the_grace_period_keeps_only_one_previous_secret()
    {
        var issued = await _fixture.NewEndpointAsync();
        var first = await _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId));

        _ = await _fixture.RotateSecret.HandleAsync(new RotateWebhookSecretCommand(issued.EndpointId));

        Assert.Equal(2, _fixture.Secrets.Count);
        Assert.DoesNotContain(issued.Secret, _fixture.Secrets.Values);
        Assert.Contains(first.Secret, _fixture.Secrets.Values);
    }

    // --- Désactivation automatique

    [Fact]
    public async Task Too_many_abandoned_deliveries_in_a_row_disable_the_endpoint_and_raise_the_event_to_notify()
    {
        _fixture.Settings.RetryPolicy = WebhookRetryPolicy.Create(1, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
        _fixture.Settings.AutoDisableAfterDeadLetters = 2;
        var issued = await _fixture.NewEndpointAsync();
        _fixture.NextResult = WebhookSendResult.Failure("http_500");
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);

        foreach (var delivery in _fixture.Deliveries.ToList())
        {
            Assert.Equal(AttemptOutcome.DeadLettered, await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(delivery.Id.Value)));
        }

        var endpoint = _fixture.EndpointById(issued.EndpointId);
        Assert.Equal(WebhookEndpointStatus.Disabled, endpoint.Status);
        var disabled = DomainAssert.Raised<WebhookEndpointAutoDisabled>(endpoint);
        Assert.Equal((2, "https://hooks.example.com/***"), (disabled.ConsecutiveDeadLetters, disabled.RedactedUrl));
    }

    [Fact]
    public async Task A_delivered_event_resets_the_count_and_a_disabled_endpoint_is_not_counted()
    {
        _fixture.Settings.RetryPolicy = WebhookRetryPolicy.Create(1, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
        _fixture.Settings.AutoDisableAfterDeadLetters = 2;
        var issued = await _fixture.NewEndpointAsync();
        _fixture.NextResult = WebhookSendResult.Failure("http_500");
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);
        _ = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(_fixture.Deliveries[0].Id.Value));
        _fixture.NextResult = WebhookSendResult.Success;
        _ = await _fixture.PublishAsync(WebhookEvents.OrderPlaced);

        _ = await _fixture.Attempt.HandleAsync(new AttemptWebhookDeliveryCommand(_fixture.Deliveries[1].Id.Value));

        var endpoint = _fixture.EndpointById(issued.EndpointId);
        Assert.Equal((WebhookEndpointStatus.Active, 0), (endpoint.Status, endpoint.ConsecutiveDeadLetters));
    }

    // --- URL masquée, auteur

    [Fact]
    public async Task An_endpoint_url_is_returned_without_its_token_and_with_a_fingerprint()
    {
        var issued = await _fixture.CreateEndpoint.HandleAsync(new CreateWebhookEndpointCommand(
            "https://hooks.example.com/in/T0K3N-S3CR3T?sig=abc", [WebhookEvents.OrderPlaced]));

        var dto = await _fixture.GetEndpoint.HandleAsync(new GetWebhookEndpointQuery(issued.EndpointId));
        var created = DomainAssert.Raised<WebhookEndpointCreated>(_fixture.EndpointById(issued.EndpointId));

        Assert.Equal("https://hooks.example.com/***", dto.Url);
        Assert.Matches("^[0-9a-f]{12}$", dto.UrlFingerprint);
        Assert.DoesNotContain("T0K3N", created.ToString(), StringComparison.Ordinal);
        Assert.Equal(_fixture.Actor, created.Actor);
    }

    [Fact]
    public async Task Key_events_carry_their_author_for_the_audit()
    {
        var issued = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["orders.view"], null));
        var credential = Assert.Single(_fixture.Credentials);
        await _fixture.Revoke.HandleAsync(new RevokeApiCredentialCommand(issued.CredentialId));

        Assert.Equal(_fixture.Actor, DomainAssert.Raised<ApiCredentialIssued>(credential).Actor);
        Assert.Equal(_fixture.Actor, DomainAssert.Raised<ApiCredentialRevoked>(credential).Actor);
    }

    // --- Envoi de test

    [Fact]
    public async Task A_test_send_is_signed_sent_at_once_and_leaves_no_delivery()
    {
        var issued = await _fixture.NewEndpointAsync();
        await _fixture.SetEnabled.HandleAsync(new SetWebhookEndpointEnabledCommand(issued.EndpointId, false));

        var result = await _fixture.SendTest.HandleAsync(new SendWebhookTestCommand(issued.EndpointId));

        Assert.True(result.Succeeded);
        Assert.Empty(_fixture.Deliveries);
        var request = Assert.Single(_fixture.Sent);
        Assert.Equal(Signature(issued.Secret, request), request.Headers[WebhookSignature.SignatureHeader]);
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        Assert.Equal(WebhookEvents.Ping, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(result.EventId, body.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_test_send_reports_the_failure_and_is_refused_for_another_restaurant()
    {
        var issued = await _fixture.NewEndpointAsync();
        _fixture.NextResult = WebhookSendResult.Failure("http_404");

        var failed = await _fixture.SendTest.HandleAsync(new SendWebhookTestCommand(issued.EndpointId));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.SendTest.HandleAsync(new SendWebhookTestCommand(issued.EndpointId)));

        Assert.Equal((false, "http_404"), (failed.Succeeded, failed.FailureReason));
        Assert.Equal(IntegrationsApplicationErrors.EndpointNotFound, foreign.Error);
    }

    [Fact]
    public void The_ping_event_cannot_be_subscribed_to()
    {
        var refused = Assert.Throws<DomainException>(() => WebhookEvents.Require(WebhookEvents.Ping));

        Assert.Equal(IntegrationsApplicationErrors.EventTypeNotPublished, refused.Error);
    }

    // --- Dernière utilisation, clés dormantes

    [Fact]
    public async Task A_key_use_is_recorded_at_most_once_per_resolution_period()
    {
        var issued = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("POS", ["orders.view"], null));
        var id = CredentialId.From(issued.CredentialId);

        _ = await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(issued.Key));
        var first = _fixture.Usage[id];
        _fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        _ = await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(issued.Key));
        var stillFirst = _fixture.Usage[id];
        _fixture.Clock.Advance(_fixture.Settings.KeyUsageResolution);
        _ = await _fixture.Authenticate.HandleAsync(new AuthenticateApiKeyQuery(issued.Key));

        Assert.Equal(first, stillFirst);
        Assert.Equal(_fixture.Clock.UtcNow, _fixture.Usage[id]);
    }

    [Fact]
    public async Task Dormant_keys_can_be_listed_on_their_own()
    {
        var old = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("Old", ["orders.view"], null));
        _fixture.Clock.Advance(_fixture.Settings.DormantKeyAfter);
        _ = await _fixture.Issue.HandleAsync(new IssueApiCredentialCommand("New", ["orders.view"], null));

        var dormant = await _fixture.ListCredentials.HandleAsync(new ListApiCredentialsQuery(DormantOnly: true));
        var all = await _fixture.ListCredentials.HandleAsync(new ListApiCredentialsQuery());

        Assert.Equal(old.CredentialId, Assert.Single(dormant).Id);
        Assert.Equal(2, all.Count);
    }
}

public sealed class WebhookSignatureTests
{
    /// <summary>Vecteur publié par la spécification Standard Webhooks : un destinataire équipé de ses bibliothèques vérifie nos envois.</summary>
    [Fact]
    public void The_signature_follows_the_standard_webhooks_test_vector()
    {
        var headers = WebhookSignature.Headers(
            "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
            "msg_p5jXN8AQM9LWM0D4loKWxJek",
            DateTimeOffset.FromUnixTimeSeconds(1614265330),
            """{"test": 2432232314}""");

        Assert.Equal("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=", headers[WebhookSignature.SignatureHeader]);
        Assert.Equal("1614265330", headers[WebhookSignature.TimestampHeader]);
    }

    [Fact]
    public void Every_secret_is_new_and_32_random_bytes()
    {
        var first = WebhookSignature.NewSecret();
        var second = WebhookSignature.NewSecret();

        Assert.NotEqual(first, second);
        Assert.Equal(32, Convert.FromBase64String(first[WebhookSignature.SecretPrefix.Length..]).Length);
    }

    [Fact]
    public void A_key_hash_is_stable_hexadecimal_and_never_the_key()
    {
        var key = ApiKeys.NewKey();

        Assert.Equal(ApiKeys.Hash(key), ApiKeys.Hash(key));
        Assert.Matches("^[0-9a-f]{64}$", ApiKeys.Hash(key).Value);
        Assert.NotEqual(ApiKeys.Hash(key), ApiKeys.Hash(ApiKeys.NewKey()));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Integrations.Application;
using Integrations.Application.Webhooks;

namespace Integrations.Api;

public sealed class IntegrationsEndpointsTests(IntegrationsApi api) : IClassFixture<IntegrationsApi>
{
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await AssertStatusAsync(response, status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await AssertStatusAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Endpoint(params string[] events) =>
        new { url = "https://hooks.example.com/in", events = events.Length == 0 ? new[] { WebhookEvents.OrderPlaced } : events };

    private static async Task<string> IssueKeyAsync(HttpClient client, params string[] scopes)
    {
        var issued = await JsonAsync(await client.PostAsJsonAsync("/integrations/api-keys", new { name = "POS", scopes }), HttpStatusCode.Created);
        return issued.GetProperty("key").GetString()!;
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/integrations/webhooks"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/integrations/webhooks"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Managing_needs_the_manage_permission_and_reading_the_view_permission()
    {
        var tenant = api.NewTenant(denied: [IntegrationsPermissions.Manage]);
        var blind = api.NewTenant(denied: [IntegrationsPermissions.View]);

        await AssertStatusAsync(await api.ActingFor(tenant).PostAsJsonAsync("/integrations/webhooks", Endpoint()), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await api.ActingFor(tenant).PostAsJsonAsync("/integrations/api-keys", new { name = "x", scopes = new[] { "orders.view" } }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await api.ActingFor(blind).GetAsync("/integrations/webhooks"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_endpoint_secret_is_returned_once_and_never_read_back()
    {
        var client = api.ActingFor(api.NewTenant());

        var created = await client.PostAsJsonAsync("/integrations/webhooks", Endpoint(WebhookEvents.OrderPlaced, WebhookEvents.KitchenTicketReady));
        var issued = await JsonAsync(created, HttpStatusCode.Created);
        var secret = issued.GetProperty("secret").GetString()!;
        var id = issued.GetProperty("endpointId").GetGuid();
        var read = await client.GetAsync(created.Headers.Location);
        var listed = await client.GetStringAsync("/integrations/webhooks");

        Assert.StartsWith("whsec_", secret, StringComparison.Ordinal);
        var endpoint = await JsonAsync(read);
        Assert.Equal(id, endpoint.GetProperty("id").GetGuid());
        Assert.False(endpoint.TryGetProperty("secret", out _));
        Assert.False(endpoint.TryGetProperty("secretReference", out _));
        Assert.DoesNotContain(secret, listed, StringComparison.Ordinal);
        Assert.DoesNotContain("whsec", listed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_restaurant_gets_not_found_for_an_endpoint_a_delivery_or_a_key()
    {
        var owner = api.ActingFor(api.NewTenant());
        var intruder = api.ActingFor(api.NewTenant());
        var endpointId = (await JsonAsync(await owner.PostAsJsonAsync("/integrations/webhooks", Endpoint()), HttpStatusCode.Created))
            .GetProperty("endpointId").GetGuid();
        var keyId = (await JsonAsync(await owner.PostAsJsonAsync("/integrations/api-keys", new { name = "POS", scopes = new[] { "orders.view" } }), HttpStatusCode.Created))
            .GetProperty("credentialId").GetGuid();

        await AssertProblemAsync(await intruder.GetAsync($"/integrations/webhooks/{endpointId}"), HttpStatusCode.NotFound, "integrations.webhook_endpoint.not_found");
        await AssertProblemAsync(await intruder.PostAsync($"/integrations/webhooks/{endpointId}/disable", null), HttpStatusCode.NotFound, "integrations.webhook_endpoint.not_found");
        await AssertProblemAsync(await intruder.GetAsync($"/integrations/webhooks/{endpointId}/deliveries"), HttpStatusCode.NotFound, "integrations.webhook_endpoint.not_found");
        await AssertProblemAsync(await intruder.PostAsync($"/integrations/api-keys/{keyId}/revoke", null), HttpStatusCode.NotFound, "integrations.api_credential.not_found");
        await AssertStatusAsync(await owner.GetAsync($"/integrations/api-keys/{keyId}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unpublished_event_or_an_internal_url_is_refused()
    {
        var client = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/integrations/webhooks", Endpoint("staff.role_created")),
            HttpStatusCode.UnprocessableEntity,
            "integrations.event_type.not_published");
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/integrations/webhooks", new { url = "https://10.0.0.5/hook", events = new[] { WebhookEvents.OrderPlaced } }),
            HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_catalog_lists_the_published_events_and_the_scopes_a_key_can_receive()
    {
        var catalog = await JsonAsync(await api.ActingFor(api.NewTenant()).GetAsync("/integrations/catalog"));

        Assert.Equal(WebhookEvents.All, catalog.GetProperty("eventTypes").EnumerateArray().Select(code => code.GetString()!).ToList());
        Assert.Equal(["orders.manage", "orders.view"], catalog.GetProperty("apiScopes").EnumerateArray().Select(scope => scope.GetString()!));
    }

    // --- Clés d'API

    [Fact]
    public async Task A_key_reaches_a_route_of_its_scope_for_the_restaurant_that_issued_it()
    {
        var tenant = api.NewTenant();
        var key = await IssueKeyAsync(api.ActingFor(tenant), "orders.view");

        var probe = await JsonAsync(await api.WithApiKey(key).GetAsync("/probe/orders"));

        Assert.Equal(tenant.Value, probe.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task A_key_is_refused_outside_its_scopes_on_routes_without_permission_and_on_the_integrations_routes()
    {
        var key = await IssueKeyAsync(api.ActingFor(api.NewTenant()), "orders.view");
        var integration = api.WithApiKey(key);

        await AssertStatusAsync(await integration.GetAsync("/probe/catalog"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await integration.GetAsync("/probe/profile"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await integration.GetAsync("/integrations/api-keys"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await integration.PostAsJsonAsync("/integrations/api-keys", new { name = "x", scopes = new[] { "orders.view" } }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_key_cannot_be_issued_with_a_scope_outside_the_catalog()
    {
        var client = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/integrations/api-keys", new { name = "x", scopes = new[] { "integrations.manage" } }),
            HttpStatusCode.UnprocessableEntity,
            "integrations.api_credential.scope_unknown");
    }

    [Fact]
    public async Task An_unknown_revoked_or_rotated_key_is_unauthorized()
    {
        var owner = api.ActingFor(api.NewTenant());
        var revoked = await JsonAsync(await owner.PostAsJsonAsync("/integrations/api-keys", new { name = "A", scopes = new[] { "orders.view" } }), HttpStatusCode.Created);
        var rotated = await JsonAsync(await owner.PostAsJsonAsync("/integrations/api-keys", new { name = "B", scopes = new[] { "orders.view" } }), HttpStatusCode.Created);
        await AssertStatusAsync(await owner.PostAsync($"/integrations/api-keys/{revoked.GetProperty("credentialId").GetGuid()}/revoke", null), HttpStatusCode.NoContent);
        var renewed = await JsonAsync(await owner.PostAsync($"/integrations/api-keys/{rotated.GetProperty("credentialId").GetGuid()}/rotate", null));

        await AssertStatusAsync(await api.WithApiKey("srk_not-a-real-key").GetAsync("/probe/orders"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.WithApiKey(revoked.GetProperty("key").GetString()!).GetAsync("/probe/orders"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.WithApiKey(rotated.GetProperty("key").GetString()!).GetAsync("/probe/orders"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.WithApiKey(renewed.GetProperty("key").GetString()!).GetAsync("/probe/orders"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_key_listing_never_shows_a_key_nor_its_hash()
    {
        var client = api.ActingFor(api.NewTenant());
        var key = await IssueKeyAsync(client, "orders.view");

        var listed = await client.GetStringAsync("/integrations/api-keys");

        Assert.DoesNotContain(key, listed, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", listed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders.view", listed, StringComparison.Ordinal);
    }

    // --- Ajouts : débit des clés, envoi de test, URL masquée, rotation, dernière utilisation

    [Fact]
    public async Task A_key_beyond_its_rate_is_told_to_retry_later()
    {
        var integration = api.WithApiKey(await IssueKeyAsync(api.ActingFor(api.NewTenant()), "orders.view"));

        for (var call = 0; call < IntegrationsApi.KeyPermitLimit; call++)
        {
            await AssertStatusAsync(await integration.GetAsync("/probe/orders"), HttpStatusCode.OK);
        }

        var refused = await integration.GetAsync("/probe/orders");
        await AssertStatusAsync(refused, HttpStatusCode.TooManyRequests);
        Assert.True(refused.Headers.RetryAfter is not null || refused.Headers.Contains("Retry-After"));
        // Une autre clé garde tout son débit.
        var other = api.WithApiKey(await IssueKeyAsync(api.ActingFor(api.NewTenant()), "orders.view"));
        await AssertStatusAsync(await other.GetAsync("/probe/orders"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_is_not_limited_by_the_api_key_rate()
    {
        var client = api.ActingFor(api.NewTenant());

        for (var call = 0; call <= IntegrationsApi.KeyPermitLimit; call++)
        {
            await AssertStatusAsync(await client.GetAsync("/integrations/webhooks"), HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_test_send_returns_its_outcome_and_creates_no_delivery()
    {
        var client = api.ActingFor(api.NewTenant());
        var endpointId = (await JsonAsync(await client.PostAsJsonAsync("/integrations/webhooks", Endpoint()), HttpStatusCode.Created))
            .GetProperty("endpointId").GetGuid();

        var result = await JsonAsync(await client.PostAsync($"/integrations/webhooks/{endpointId}/test", null));
        var deliveries = (await JsonAsync(await client.GetAsync($"/integrations/webhooks/{endpointId}/deliveries"))).GetProperty("items");

        Assert.True(result.GetProperty("succeeded").GetBoolean());
        var eventId = result.GetProperty("eventId").GetGuid();
        Assert.Contains(api.Sent, request => request.Headers["webhook-id"] == eventId.ToString("D"));
        Assert.Empty(deliveries.EnumerateArray());
    }

    [Fact]
    public async Task An_endpoint_url_is_never_returned_with_its_path_or_query()
    {
        var client = api.ActingFor(api.NewTenant());
        var created = await client.PostAsJsonAsync(
            "/integrations/webhooks", new { url = "https://hooks.example.com/t/S3CRET-TOKEN?sig=xyz", events = new[] { WebhookEvents.OrderPlaced } });
        await AssertStatusAsync(created, HttpStatusCode.Created);

        var listed = await client.GetStringAsync("/integrations/webhooks");

        Assert.DoesNotContain("S3CRET-TOKEN", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=xyz", listed, StringComparison.Ordinal);
        Assert.Contains("https://hooks.example.com/***", listed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rotation_keeps_the_previous_secret_for_a_while_unless_it_is_immediate()
    {
        var client = api.ActingFor(api.NewTenant());
        var endpointId = (await JsonAsync(await client.PostAsJsonAsync("/integrations/webhooks", Endpoint()), HttpStatusCode.Created))
            .GetProperty("endpointId").GetGuid();

        await AssertStatusAsync(await client.PostAsync($"/integrations/webhooks/{endpointId}/rotate-secret", null), HttpStatusCode.OK);
        var graced = await JsonAsync(await client.GetAsync($"/integrations/webhooks/{endpointId}"));
        await AssertStatusAsync(await client.PostAsync($"/integrations/webhooks/{endpointId}/rotate-secret?immediate=true", null), HttpStatusCode.OK);
        var immediate = await JsonAsync(await client.GetAsync($"/integrations/webhooks/{endpointId}"));

        Assert.Equal(JsonValueKind.String, graced.GetProperty("previousSecretValidUntilUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, immediate.GetProperty("previousSecretValidUntilUtc").ValueKind);
    }

    [Fact]
    public async Task A_key_shows_when_it_was_last_used()
    {
        var owner = api.ActingFor(api.NewTenant());
        var issued = await JsonAsync(await owner.PostAsJsonAsync("/integrations/api-keys", new { name = "POS", scopes = new[] { "orders.view" } }), HttpStatusCode.Created);
        var id = issued.GetProperty("credentialId").GetGuid();
        var before = await JsonAsync(await owner.GetAsync($"/integrations/api-keys/{id}"));

        await AssertStatusAsync(await api.WithApiKey(issued.GetProperty("key").GetString()!).GetAsync("/probe/orders"), HttpStatusCode.OK);
        var after = await JsonAsync(await owner.GetAsync($"/integrations/api-keys/{id}"));

        Assert.Equal(JsonValueKind.Null, before.GetProperty("lastUsedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, after.GetProperty("lastUsedAtUtc").ValueKind);
        Assert.False(after.GetProperty("isDormant").GetBoolean());
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Notifications.Application;
using Notifications.Domain;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Api;

/// <summary>Tests d'intégration et multi-tenant (étapes 30-31) : le module par HTTP, de la configuration à la remise.</summary>
public sealed class NotificationsEndpointsTests(NotificationsApi api) : IClassFixture<NotificationsApi>
{
    private const string ApiKey = "fake_SUPER_SECRET_key";
    private const string WebhookSecret = "whsec_SUPER_SECRET";

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await AssertStatusAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response) =>
        (await JsonAsync(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    /// <summary>La plateforme propose le prestataire simulé pour l'email (une seule fois pour la classe).</summary>
    private async Task<Guid> FakeEmailProviderAsync()
    {
        var admin = api.Admin();
        var providers = await JsonAsync(await admin.GetAsync("/admin/notification-providers"));
        if (providers.EnumerateArray().FirstOrDefault(provider => provider.GetProperty("code").GetString() == "FAKE") is { ValueKind: JsonValueKind.Object } existing)
        {
            return existing.GetProperty("id").GetGuid();
        }

        var id = await CreatedIdAsync(await admin.PostAsJsonAsync("/admin/notification-providers", new
        {
            code = "fake",
            name = "Fake mail",
            channel = "Email",
            configurationType = "PlatformOrTenant",
            capability = new { features = new[] { "Text", "DeliveryStatus" } },
        }));
        await AssertStatusAsync(await admin.PostAsync($"/admin/notification-providers/{id}/enable", null), HttpStatusCode.NoContent);
        return id;
    }

    /// <summary>Un restaurant qui envoie ses emails par son propre compte chez le prestataire simulé, avec un template.</summary>
    private async Task<(HttpClient Client, Guid ConfigurationId)> RestaurantReadyAsync(TenantId tenant)
    {
        var providerId = await FakeEmailProviderAsync();
        var client = api.ActingFor(tenant);
        var configurationId = await CreatedIdAsync(await client.PostAsJsonAsync($"/notification-configurations/{providerId}/configure", new { mode = "TenantManaged" }));
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/notification-configurations/{configurationId}/credentials",
                new { credentials = new Dictionary<string, string> { ["api_key"] = ApiKey, ["webhook_secret"] = WebhookSecret } }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/notification-configurations/{configurationId}/enable", null), HttpStatusCode.NoContent);
        var templateId = await CreatedIdAsync(await client.PostAsJsonAsync("/notification-templates", new
        {
            key = "order.ready", channel = "Email", language = "fr", subject = "Commande {{order.number}}", body = "Bonjour {{customer.firstName}}, la commande {{order.number}} est prête.",
        }));
        await AssertStatusAsync(await client.PostAsync($"/notification-templates/{templateId}/publish", null), HttpStatusCode.NoContent);
        return (client, configurationId);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Guid customerId, string? key = null) =>
        client.PostAsJsonAsync("/notifications", new
        {
            type = "order.ready", channel = "Email", customerId, variables = new Dictionary<string, string> { ["order.number"] = "A-12" }, idempotencyKey = key,
        });

    [Fact]
    public async Task A_notification_goes_from_the_api_to_the_provider_and_is_delivered_by_its_webhook()
    {
        var tenant = api.NewTenant();
        var (client, configurationId) = await RestaurantReadyAsync(tenant);
        var customer = api.Customer(tenant, "awa@example.com");

        var sent = await JsonAsync(await SendAsync(client, customer.Value));
        var notificationId = sent.GetProperty("notificationIds")[0].GetGuid();
        await api.Worker.DispatchDueAsync();

        var message = Assert.Single(api.Outbox.Sent, candidate => candidate.NotificationId.Value == notificationId);
        Assert.Equal("Bonjour Awa, la commande A-12 est prête.", message.Body);
        Assert.Equal("Commande A-12", message.Subject);

        var body = Encoding.UTF8.GetBytes($$"""{"events":[{"id":"evt-{{notificationId:N}}","messageId":"{{message.MessageId.Value}}","kind":"delivered"}]}""");
        using var webhook = new HttpRequestMessage(HttpMethod.Post, $"/notifications/webhooks/fake/{configurationId}") { Content = new ByteArrayContent(body) };
        webhook.Headers.Add(FakeWebhookReader.SignatureHeader, FakeWebhookReader.Sign(WebhookSecret, body));
        await AssertStatusAsync(await api.Anonymous().SendAsync(webhook), HttpStatusCode.OK);
        await api.Worker.ApplyWebhooksAsync();

        var details = await JsonAsync(await client.GetAsync($"/notifications/{notificationId}"));
        Assert.Equal("Delivered", details.GetProperty("status").GetString());
        Assert.Equal("a***@example.com", details.GetProperty("recipient").GetString());
        Assert.Equal("Delivered", details.GetProperty("deliveries")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_forged_webhook_is_refused()
    {
        var tenant = api.NewTenant();
        var (_, configurationId) = await RestaurantReadyAsync(tenant);
        var body = Encoding.UTF8.GetBytes("""{"events":[{"id":"evt-forged","messageId":"fake-x","kind":"delivered"}]}""");
        using var webhook = new HttpRequestMessage(HttpMethod.Post, $"/notifications/webhooks/fake/{configurationId}") { Content = new ByteArrayContent(body) };
        webhook.Headers.Add(FakeWebhookReader.SignatureHeader, FakeWebhookReader.Sign("not-the-secret", body));

        await AssertStatusAsync(await api.Anonymous().SendAsync(webhook), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Secrets_are_never_returned_by_any_route()
    {
        var tenant = api.NewTenant();
        var (client, _) = await RestaurantReadyAsync(tenant);
        var customer = api.Customer(tenant, "kofi@example.com");
        await SendAsync(client, customer.Value);
        await api.Worker.DispatchDueAsync();

        var responses = new[]
        {
            await (await client.GetAsync("/notification-configurations")).Content.ReadAsStringAsync(),
            await (await client.GetAsync("/notification-configurations/providers")).Content.ReadAsStringAsync(),
            await (await client.GetAsync("/notifications")).Content.ReadAsStringAsync(),
            await (await api.Admin().GetAsync("/admin/notification-providers")).Content.ReadAsStringAsync(),
            await (await api.Admin().GetAsync("/admin/notification-providers/integrations")).Content.ReadAsStringAsync(),
        };

        Assert.All(responses, response =>
        {
            Assert.DoesNotContain(ApiKey, response, StringComparison.Ordinal);
            Assert.DoesNotContain(WebhookSecret, response, StringComparison.Ordinal);
            Assert.DoesNotContain("notifications/tenant/", response, StringComparison.Ordinal);
            Assert.DoesNotContain("kofi@example.com", response, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Tenant_A_cannot_see_nor_change_the_notifications_and_templates_of_tenant_B()
    {
        var tenantB = api.NewTenant();
        var (clientB, configurationB) = await RestaurantReadyAsync(tenantB);
        var customerB = api.Customer(tenantB, "b@example.com");
        var notificationB = (await JsonAsync(await SendAsync(clientB, customerB.Value))).GetProperty("notificationIds")[0].GetGuid();
        var templateB = (await JsonAsync(await clientB.GetAsync("/notification-templates")))
            .EnumerateArray().First(template => !template.GetProperty("isPlatform").GetBoolean()).GetProperty("id").GetGuid();

        var clientA = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await clientA.GetAsync($"/notifications/{notificationB}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await clientA.PostAsync($"/notifications/{notificationB}/cancel", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await clientA.GetAsync($"/notification-templates/{templateB}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await clientA.PostAsync($"/notification-templates/{templateB}/disable", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await clientA.PostAsync($"/notification-configurations/{configurationB}/disable", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(
            await clientA.PostAsJsonAsync("/notifications", new { type = "order.ready", channel = "Email", customerId = customerB.Value }),
            HttpStatusCode.NotFound);
        Assert.Empty((await JsonAsync(await clientA.GetAsync("/notifications"))).EnumerateArray());
        Assert.Empty((await JsonAsync(await clientA.GetAsync("/notification-configurations"))).EnumerateArray());
    }

    [Fact]
    public async Task Routes_need_a_restaurant_permissions_and_admin_routes_need_an_administrator()
    {
        var blind = api.ActingFor(api.NewTenant(NotificationsPermissions.View, NotificationsPermissions.Configure));

        await AssertStatusAsync(await api.Anonymous().GetAsync("/notifications"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/notifications"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await blind.GetAsync("/notifications"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await blind.PostAsJsonAsync("/notification-templates", new { key = "order.ready", channel = "Sms", language = "fr", body = "x" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await api.ActingFor(api.NewTenant()).GetAsync("/admin/notification-providers"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_replayed_request_does_not_send_twice()
    {
        var tenant = api.NewTenant();
        var (client, _) = await RestaurantReadyAsync(tenant);
        var customer = api.Customer(tenant, "replay@example.com");

        await SendAsync(client, customer.Value, key: "pos-1");
        var replay = await JsonAsync(await SendAsync(client, customer.Value, key: "pos-1"));
        await api.Worker.DispatchDueAsync();

        Assert.Equal("Duplicate", replay.GetProperty("skipped").GetString());
        Assert.Single(api.Outbox.Sent, message => message.Address == "replay@example.com");
    }

    [Fact]
    public async Task An_in_app_notification_lands_in_the_inbox_of_its_user_only()
    {
        var tenant = api.NewTenant();
        var user = UserId.New();
        var staff = api.ActingFor(tenant, user);
        var templateId = await CreatedIdAsync(await staff.PostAsJsonAsync("/notification-templates", new
        {
            key = "staff.new_reservation", channel = "InApp", language = "fr", subject = "Réservation", body = "Nouvelle réservation pour {{reservation.guestCount}}.",
        }));
        await AssertStatusAsync(await staff.PostAsync($"/notification-templates/{templateId}/publish", null), HttpStatusCode.NoContent);

        var sent = await JsonAsync(await staff.PostAsJsonAsync("/notifications", new
        {
            type = "staff.new_reservation", channel = "InApp", userId = user.Value, variables = new Dictionary<string, string> { ["reservation.guestCount"] = "4" },
        }));
        var id = sent.GetProperty("notificationIds")[0].GetGuid();
        await api.Worker.DispatchDueAsync();

        var inbox = (await JsonAsync(await staff.GetAsync("/me/notifications"))).GetProperty("items");
        var other = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/me/notifications"))).GetProperty("items");
        await AssertStatusAsync(await api.ActingFor(tenant).PostAsync($"/me/notifications/{id}/read", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await staff.PostAsync($"/me/notifications/{id}/archive", null), HttpStatusCode.NoContent);
        var archived = (await JsonAsync(await staff.GetAsync("/me/notifications"))).GetProperty("items");

        var item = Assert.Single(inbox.EnumerateArray());
        Assert.Equal("Nouvelle réservation pour 4.", item.GetProperty("body").GetString());
        Assert.Equal("Unread", item.GetProperty("state").GetString());
        Assert.Empty(other.EnumerateArray());
        Assert.Empty(archived.EnumerateArray());
    }

    [Fact]
    public async Task A_dead_letter_can_be_inspected_and_retried_by_the_platform()
    {
        var tenant = api.NewTenant();
        var (client, _) = await RestaurantReadyAsync(tenant);
        var customer = api.Customer(tenant, "dead@example.com");
        var id = (await JsonAsync(await SendAsync(client, customer.Value))).GetProperty("notificationIds")[0].GetGuid();
        api.Outbox.FailNext(NotificationFailure.Permanent(NotificationFailure.InvalidEmail, "550 dead@example.com unknown"));
        await api.Worker.DispatchDueAsync();

        var deadLetters = (await JsonAsync(await api.Admin().GetAsync("/admin/notification-dead-letters"))).GetProperty("items");
        var entry = deadLetters.EnumerateArray().Single(candidate => candidate.GetProperty("id").GetGuid() == id);
        Assert.Equal("INVALID_EMAIL", entry.GetProperty("lastFailureCode").GetString());
        Assert.DoesNotContain("dead@example.com", deadLetters.GetRawText(), StringComparison.Ordinal);

        await AssertStatusAsync(await api.Admin().PostAsync($"/admin/notification-dead-letters/{id}/retry", null), HttpStatusCode.NoContent);
        await api.Worker.DispatchDueAsync();

        Assert.Single(api.Outbox.Sent, message => message.NotificationId.Value == id);
        Assert.Equal("Sent", (await JsonAsync(await client.GetAsync($"/notifications/{id}"))).GetProperty("status").GetString());
    }
}

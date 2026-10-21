using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Payments.Infrastructure.Gateways;

namespace Payments.Api;

public sealed class PaymentsEndpointsTests(PaymentsApi api) : IClassFixture<PaymentsApi>
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

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Un restaurant qui accepte espèces et mobile money, et une commande à payer.</summary>
    private async Task<(TenantId Tenant, HttpClient Client, Guid Order)> GivenOrderAsync(decimal total = 7000m)
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        foreach (var provider in new[] { "cash", "mobile_money" })
        {
            await CreatedAsync(await client.PostAsJsonAsync("/payments/configurations", new { provider }));
        }

        return (tenant, client, api.NewOrder(tenant, total));
    }

    /// <summary>Le restaurant accepte le sandbox avec son secret de webhook ; renvoie l'id de sa configuration.</summary>
    private static async Task<Guid> AcceptSandboxAsync(HttpClient client)
    {
        var method = (await CreatedAsync(await client.PostAsJsonAsync("/payments/configurations", new { provider = "sandbox" }))).GetProperty("id").GetGuid();
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/payments/configurations/{method}/credentials", new { credentials = new Dictionary<string, string> { ["webhook_secret"] = PaymentsApi.SandboxSecret } }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/payments/configurations/{method}/enable", null), HttpStatusCode.NoContent);
        return method;
    }

    private Task<HttpResponseMessage> SendWebhookAsync(Guid method, string body, string? signature = null, string provider = "sandbox")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/payments/webhooks/{provider}/{method}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(SandboxSignature.Header, signature ?? SandboxSignature.Sign(PaymentsApi.SandboxSecret, body, api.Clock.UtcNow));
        return api.Anonymous().SendAsync(request);
    }

    private static string Captured(string reference, decimal amount) =>
        JsonSerializer.Serialize(new { id = "evt_" + Guid.NewGuid().ToString("N"), type = "payment.captured", reference, amount, currency = "XOF" });

    /// <summary>Le webhook est appliqué en arrière-plan : on relance le traitement jusqu'à voir le résultat.</summary>
    private async Task<JsonElement> EventuallyAsync(HttpClient client, Guid paymentId, string status)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await api.ProcessWebhooksAsync();
            var payment = await client.GetFromJsonAsync<JsonElement>($"/payments/{paymentId}");
            if (payment.GetProperty("status").GetString() == status)
            {
                return payment;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Payment {paymentId} never reached {status}.");
    }

    [Fact]
    public async Task Staff_routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync($"/payments?orderId={Guid.NewGuid()}"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync($"/payments?orderId={Guid.NewGuid()}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cash_at_the_counter_settles_the_order_and_is_refunded_by_hand()
    {
        var (_, client, order) = await GivenOrderAsync();

        var first = await CreatedAsync(await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-1", amount = 4000 }));
        await CreatedAsync(await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "mobile_money", reference = "MOMO-77" }));
        var refund = await CreatedAsync(await client.PostAsJsonAsync($"/payments/{first.GetProperty("id").GetGuid()}/refunds", new { reason = "Cold dish", amount = 1000 }));
        var view = await client.GetFromJsonAsync<JsonElement>($"/payments?orderId={order}");

        Assert.NotEqual(Guid.Empty, refund.GetProperty("id").GetGuid());
        Assert.Equal(7000m, view.GetProperty("total").GetDecimal());
        Assert.Equal(6000m, view.GetProperty("settledAmount").GetDecimal());
        Assert.Equal(1000m, view.GetProperty("amountDue").GetDecimal());
        var payments = view.GetProperty("payments").EnumerateArray().ToList();
        Assert.Equal(new[] { "PartiallyRefunded", "Captured" }, payments.Select(payment => payment.GetProperty("status").GetString()!).ToArray());
        Assert.Equal("Succeeded", payments[0].GetProperty("refunds")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_online_payment_is_paid_when_the_signed_webhook_of_the_provider_arrives()
    {
        var (_, client, order) = await GivenOrderAsync();
        var method = await AcceptSandboxAsync(client);

        var started = await CreatedAsync(await client.PostAsJsonAsync("/payments/online", new { orderId = order, provider = "sandbox", paymentMethod = "Card" }));
        var paymentId = started.GetProperty("paymentId").GetGuid();
        var reference = started.GetProperty("providerReference").GetString()!;
        var pending = await client.GetFromJsonAsync<JsonElement>($"/payments/{paymentId}");
        var webhookPath = (await client.GetFromJsonAsync<JsonElement>("/payments/configurations")).EnumerateArray()
            .Single(candidate => candidate.GetProperty("provider").GetString() == "sandbox").GetProperty("webhookPath").GetString();
        await AssertStatusAsync(await SendWebhookAsync(method, Captured(reference, 7000m)), HttpStatusCode.OK);
        var paid = await EventuallyAsync(client, paymentId, "Captured");

        Assert.StartsWith("sbx_", reference, StringComparison.Ordinal);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.False(pending.GetProperty("isOffline").GetBoolean());
        Assert.Equal(7000m, paid.GetProperty("settledAmount").GetDecimal());
        Assert.Equal($"/payments/webhooks/sandbox/{method}", webhookPath);
    }

    [Fact]
    public async Task Webhooks_need_the_signature_of_the_restaurant_and_its_address_but_no_token()
    {
        var (_, client, _) = await GivenOrderAsync();
        var method = await AcceptSandboxAsync(client);
        var body = Captured("sbx_unknown", 1000m);

        await AssertStatusAsync(await SendWebhookAsync(method, body, signature: "t=1,v1=" + new string('a', 64)), HttpStatusCode.BadRequest);
        await AssertStatusAsync(await SendWebhookAsync(method, body, provider: "stripe"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await SendWebhookAsync(Guid.NewGuid(), body), HttpStatusCode.NotFound);
        await AssertStatusAsync(await SendWebhookAsync(method, body), HttpStatusCode.OK);
        await AssertStatusAsync(await SendWebhookAsync(method, body), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_restaurant_chooses_its_methods_among_those_the_platform_offers()
    {
        var (_, client, order) = await GivenOrderAsync();

        var offered = await client.GetFromJsonAsync<JsonElement>("/payments/providers");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "card_terminal", reference = "TPE-1" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.method.not_accepted");
        var sandbox = (await CreatedAsync(await client.PostAsJsonAsync("/payments/configurations", new { provider = "sandbox" }))).GetProperty("id").GetGuid();
        await AssertProblemAsync(
            await client.PostAsync($"/payments/configurations/{sandbox}/enable", null), HttpStatusCode.UnprocessableEntity, "payments.configuration.credentials_missing");
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/payments/configurations/{sandbox}/credentials", new { credentials = new Dictionary<string, string> { ["secret_key"] = "x" } }),
            HttpStatusCode.UnprocessableEntity,
            "payments.configuration.credential_fields_invalid");
        var methods = await client.GetFromJsonAsync<JsonElement>("/payments/configurations");

        var codes = offered.EnumerateArray().Select(provider => provider.GetProperty("code").GetString()!).ToArray();
        Assert.Contains("sandbox", codes);
        Assert.Contains("cash", codes);
        Assert.Equal(
            "webhook_secret",
            Assert.Single(offered.EnumerateArray().Single(provider => provider.GetProperty("code").GetString() == "sandbox").GetProperty("credentialFields").EnumerateArray().ToList()).GetString());
        Assert.Equal(3, methods.GetArrayLength());
        Assert.DoesNotContain(PaymentsApi.SandboxSecret, methods.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var (tenant, client, order) = await GivenOrderAsync();
        await CreatedAsync(await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-1" }));

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-2" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.order.nothing_due");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/online", new { orderId = api.NewOrder(tenant), provider = "cash" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.method.type_mismatch");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/online", new { orderId = api.NewOrder(tenant), provider = "stripe" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.method.not_accepted");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = api.NewOrder(tenant, acceptsPayments: false), provider = "cash", reference = "T-4" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.order.not_payable");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = Guid.NewGuid(), provider = "cash", reference = "T-5" }),
            HttpStatusCode.NotFound,
            "payments.order.not_found");
    }

    [Fact]
    public async Task The_payments_of_another_restaurant_are_invisible()
    {
        var (_, owner, order) = await GivenOrderAsync();
        var payment = (await CreatedAsync(await owner.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-1" })))
            .GetProperty("id").GetGuid();
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/payments/{payment}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.GetAsync($"/payments?orderId={order}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsJsonAsync($"/payments/{payment}/refunds", new { reason = "Theft" }), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Writes_need_their_permission_but_reads_stay_open_to_members()
    {
        var (tenant, client, order) = await GivenOrderAsync();
        var payment = (await CreatedAsync(await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-1", amount = 1000 })))
            .GetProperty("id").GetGuid();
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.GetAsync($"/payments/{payment}"), HttpStatusCode.OK);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-2" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsJsonAsync($"/payments/{payment}/refunds", new { reason = "x" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsJsonAsync($"/payments/{payment}/cancel", new { reason = "x" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync("/payments/configurations"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.PostAsJsonAsync("/payments/configurations", new { provider = "card_terminal" }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_payments_but_takes_no_new_one()
    {
        var (tenant, client, order) = await GivenOrderAsync();
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/payments?orderId={order}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/payments/offline", new { orderId = order, provider = "cash", reference = "T-1" }),
            HttpStatusCode.UnprocessableEntity,
            "payments.tenant.not_writable");
    }

    [Fact]
    public async Task Credentials_and_their_vault_reference_never_leave_the_api()
    {
        var (_, client, _) = await GivenOrderAsync();
        await AcceptSandboxAsync(client);
        var catalog = await api.ActingFor(null).GetFromJsonAsync<JsonElement>("/admin/payment-providers");
        var sandbox = catalog.EnumerateArray().Single(provider => provider.GetProperty("code").GetString() == "sandbox").GetProperty("id").GetGuid();

        var configurations = (await client.GetFromJsonAsync<JsonElement>("/payments/configurations")).GetRawText();
        // Identifiants de la plateforme (abonnements) : acceptés pour un prestataire en ligne, jamais renvoyés.
        await AssertStatusAsync(
            await api.ActingFor(null).PutAsJsonAsync(
                $"/admin/payment-providers/{sandbox}/credentials", new { credentials = new Dictionary<string, string> { ["webhook_secret"] = "platform-secret" } }),
            HttpStatusCode.NoContent);
        var platformCatalog = (await api.ActingFor(null).GetFromJsonAsync<JsonElement>("/admin/payment-providers")).GetRawText();
        Assert.DoesNotContain("platform-secret", platformCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("payments/platform/", platformCatalog, StringComparison.Ordinal);

        Assert.DoesNotContain(PaymentsApi.SandboxSecret, configurations, StringComparison.Ordinal);
        Assert.DoesNotContain("payments/tenant/", configurations, StringComparison.Ordinal);
        Assert.DoesNotContain("secretReference", configurations, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasCredentials\":true", configurations, StringComparison.Ordinal);
    }
}

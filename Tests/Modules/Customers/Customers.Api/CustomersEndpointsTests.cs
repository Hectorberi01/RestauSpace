using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Customers.Application;

namespace Customers.Api;

public sealed class CustomersEndpointsTests(CustomersApi api) : IClassFixture<CustomersApi>
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name = "Awa Dossou", string? email = "awa@example.com") =>
        client.PostAsJsonAsync("/customers", new { name, email, tags = new[] { "VIP" } });

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/customers"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/customers"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_is_created_found_edited_and_read_back()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await CreatedIdAsync(await CreateAsync(client));

        await AssertStatusAsync(await client.PutAsJsonAsync($"/customers/{id}/name", new { name = "Awa D." }), HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/customers/{id}/contact", new { email = "awa@example.com", phone = "+229 97 00 00 00" }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsJsonAsync($"/customers/{id}/tags", new { tag = "Table près de la fenêtre" }), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.DeleteAsync($"/customers/{id}/tags?tag=vip"), HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/customers/{id}/preferences", new { language = "fr", marketingChannels = new[] { "Sms" } }),
            HttpStatusCode.NoContent);

        var customer = await JsonAsync(await client.GetAsync($"/customers/{id}"));
        var byPhone = (await JsonAsync(await client.GetAsync("/customers?phone=%2B22997000000"))).GetProperty("items");
        var byName = (await JsonAsync(await client.GetAsync("/customers?name=awa"))).GetProperty("items");

        Assert.Equal("Awa D.", customer.GetProperty("name").GetString());
        Assert.Equal("+22997000000", customer.GetProperty("phone").GetString());
        Assert.Equal("Active", customer.GetProperty("status").GetString());
        Assert.Equal("EmailOrPhone", customer.GetProperty("contactRequirement").GetString());
        Assert.Equal(new[] { "table près de la fenêtre" }, customer.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ToArray());
        Assert.Equal("Sms", customer.GetProperty("preferences").GetProperty("marketingChannels")[0].GetString());
        Assert.Equal(id, Assert.Single(byPhone.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(id, Assert.Single(byName.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Consents_are_recorded_with_their_history_and_drive_the_marketing_permission()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await CreatedIdAsync(await CreateAsync(client));

        var before = await JsonAsync(await client.GetAsync($"/customers/{id}/consents/Email/marketing-permission"));
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/customers/{id}/consents/grant", new { channel = "Email", source = "web-signup-form" }),
            HttpStatusCode.NoContent);
        var granted = await JsonAsync(await client.GetAsync($"/customers/{id}/consents/Email/marketing-permission"));
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/customers/{id}/consents/withdraw", new { channel = "Email", reason = "UnsubscribeLink", source = "unsubscribe-link" }),
            HttpStatusCode.NoContent);
        var consents = await JsonAsync(await client.GetAsync($"/customers/{id}/consents"));

        Assert.Equal((false, "no_consent"), (before.GetProperty("canSend").GetBoolean(), before.GetProperty("reason").GetString()));
        Assert.True(granted.GetProperty("canSend").GetBoolean());
        var consent = Assert.Single(consents.EnumerateArray());
        Assert.Equal(("Email", "Withdrawn", "UnsubscribeLink"), (
            consent.GetProperty("channel").GetString(),
            consent.GetProperty("status").GetString(),
            consent.GetProperty("withdrawalReason").GetString()));
        Assert.Equal(2, consent.GetProperty("history").GetArrayLength());
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/customers/{id}/consents/grant", new { channel = "Sms", source = "Oui au téléphone" }),
            HttpStatusCode.UnprocessableEntity,
            "customers.consent.source_invalid");
    }

    [Fact]
    public async Task Anonymizing_erases_the_customer_for_good()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await CreatedIdAsync(await CreateAsync(client));
        await AssertStatusAsync(await client.PostAsJsonAsync($"/customers/{id}/consents/grant", new { channel = "Email" }), HttpStatusCode.NoContent);

        await AssertStatusAsync(await client.PostAsync($"/customers/{id}/anonymize", null), HttpStatusCode.NoContent);

        var customer = await JsonAsync(await client.GetAsync($"/customers/{id}"));
        var consent = Assert.Single((await JsonAsync(await client.GetAsync($"/customers/{id}/consents"))).EnumerateArray());
        Assert.True(customer.GetProperty("isAnonymized").GetBoolean());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("email").ValueKind);
        Assert.Equal(0, customer.GetProperty("tags").GetArrayLength());
        Assert.Equal("CustomerAnonymized", consent.GetProperty("withdrawalReason").GetString());
        Assert.Empty((await JsonAsync(await client.GetAsync("/customers?email=awa@example.com"))).GetProperty("items").EnumerateArray());
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/customers/{id}/name", new { name = "Retour" }),
            HttpStatusCode.UnprocessableEntity,
            "customers.customer.anonymized");
    }

    [Fact]
    public async Task The_contact_policy_is_set_per_restaurant_and_applies_to_new_customers()
    {
        var client = api.ActingFor(api.NewTenant());

        var initial = await JsonAsync(await client.GetAsync("/customers/settings/contact-policy"));
        await AssertProblemAsync(await CreateAsync(client, "Sans contact", null), HttpStatusCode.UnprocessableEntity, "customers.customer.contact_required");
        await AssertStatusAsync(await client.PutAsJsonAsync("/customers/settings/contact-policy", new { requirement = "None" }), HttpStatusCode.NoContent);
        var walkIn = await CreatedIdAsync(await CreateAsync(client, "Client de passage", null));

        Assert.Equal("EmailOrPhone", initial.GetProperty("requirement").GetString());
        Assert.Equal("None", (await JsonAsync(await client.GetAsync($"/customers/{walkIn}"))).GetProperty("contactRequirement").GetString());
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/customers", new { name = "Mauvais", email = "pas-un-email" }),
            HttpStatusCode.UnprocessableEntity,
            "common.email.invalid");
    }

    [Fact]
    public async Task A_customer_of_another_restaurant_does_not_exist_for_us()
    {
        var mine = await CreatedIdAsync(await CreateAsync(api.ActingFor(api.NewTenant())));
        var other = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(await other.GetAsync($"/customers/{mine}"), HttpStatusCode.NotFound, "customers.customer.not_found");
        await AssertProblemAsync(await other.PostAsync($"/customers/{mine}/anonymize", null), HttpStatusCode.NotFound, "customers.customer.not_found");
        Assert.Empty((await JsonAsync(await other.GetAsync("/customers"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Personal_data_needs_its_own_permissions_even_to_be_read()
    {
        var blind = api.ActingFor(api.NewTenant(true, CustomersPermissions.View));
        var noEraser = api.ActingFor(api.NewTenant(true, CustomersPermissions.Anonymize, CustomersPermissions.Configure));
        var id = await CreatedIdAsync(await CreateAsync(noEraser));

        await AssertStatusAsync(await blind.GetAsync("/customers"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await noEraser.GetAsync($"/customers/{id}"), HttpStatusCode.OK);
        await AssertStatusAsync(await noEraser.PostAsync($"/customers/{id}/anonymize", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await noEraser.PutAsJsonAsync("/customers/settings/contact-policy", new { requirement = "None" }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_customers_and_can_still_erase_one()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var id = await CreatedIdAsync(await CreateAsync(client));
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/customers/{id}"), HttpStatusCode.OK);
        await AssertProblemAsync(await CreateAsync(client, "Kofi"), HttpStatusCode.UnprocessableEntity, "customers.tenant.not_writable");
        await AssertStatusAsync(await client.PostAsync($"/customers/{id}/anonymize", null), HttpStatusCode.NoContent);
    }
}

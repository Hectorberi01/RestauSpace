using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tenants.Api;

public sealed class TenantsEndpointsTests(TenantsApi api) : IClassFixture<TenantsApi>
{
    private HttpClient Client => api.Client;

    [Fact]
    public async Task Post_creates_a_trial_tenant_and_points_to_it()
    {
        var response = await Client.PostAsJsonAsync("/admin/tenants", new
        {
            name = " Chez Hector ", defaultCurrencyCode = "xof", timeZoneId = "Africa/Porto-Novo", pricesIncludeTax = false,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await ReadAsync(response)).GetProperty("id").GetGuid();
        Assert.Equal($"/admin/tenants/{id}", response.Headers.Location!.AbsolutePath);

        var tenant = await Client.GetFromJsonAsync<JsonElement>(response.Headers.Location);
        Assert.Equal(id, tenant.GetProperty("id").GetGuid());
        Assert.Equal("Chez Hector", tenant.GetProperty("name").GetString());
        Assert.Equal("Trial", tenant.GetProperty("status").GetString());
        Assert.True(tenant.GetProperty("canAcceptBusinessWrites").GetBoolean());
        Assert.Equal("XOF", tenant.GetProperty("defaultCurrencyCode").GetString());
        Assert.Equal("Africa/Porto-Novo", tenant.GetProperty("timeZoneId").GetString());
        Assert.False(tenant.GetProperty("pricesIncludeTax").GetBoolean());
        Assert.Equal("Chez Hector", tenant.GetProperty("branding").GetProperty("displayName").GetString());
        Assert.Equal(FakeClock.DefaultNow, tenant.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, tenant.GetProperty("suspensionReason").ValueKind);
    }

    [Fact]
    public async Task Current_tenant_is_read_from_the_authenticated_context_only()
    {
        var id = await CreateAsync();
        api.CurrentTenantId = TenantId.From(id);
        try
        {
            var tenant = await Client.GetFromJsonAsync<JsonElement>("/tenant");
            Assert.Equal(id, tenant.GetProperty("id").GetGuid());

            api.CurrentTenantId = null;
            await AssertProblemAsync(await Client.GetAsync("/tenant"), HttpStatusCode.Forbidden, "common.tenant.required");
        }
        finally
        {
            api.CurrentTenantId = null;
        }
    }

    [Fact]
    public async Task Post_defaults_to_tax_inclusive_prices()
    {
        var id = await CreateAsync();

        Assert.True((await GetAsync(id)).GetProperty("pricesIncludeTax").GetBoolean());
    }

    [Theory]
    [InlineData(null, "XOF", "Africa/Porto-Novo", "tenants.tenant_name.empty")]
    [InlineData("Chez Hector", "???", "Africa/Porto-Novo", "common.currency.unknown")]
    [InlineData("Chez Hector", "XOF", "Romance Standard Time", "common.time_zone.unknown")]
    public async Task Post_with_invalid_data_is_a_422_carrying_the_domain_code(
        string? name, string currency, string timeZone, string expectedCode)
    {
        var response = await Client.PostAsJsonAsync(
            "/admin/tenants", new { name, defaultCurrencyCode = currency, timeZoneId = timeZone });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, expectedCode);
    }

    [Fact]
    public async Task Control_characters_in_names_are_a_422_not_a_storage_failure()
    {
        var id = await CreateAsync();

        await AssertProblemAsync(
            await Client.PostAsJsonAsync(
                "/admin/tenants", new { name = "Che\0z", defaultCurrencyCode = "XOF", timeZoneId = "Africa/Porto-Novo" }),
            HttpStatusCode.UnprocessableEntity,
            "tenants.tenant_name.invalid_characters");
        await AssertProblemAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/branding", new { displayName = "Che\0z" }),
            HttpStatusCode.UnprocessableEntity,
            "tenants.branding.display_name_invalid_characters");
    }

    [Fact]
    public async Task A_logo_url_typed_with_a_control_character_is_saved_escaped()
    {
        var id = await CreateAsync();

        await AssertNoContentAsync(await Client.PutAsJsonAsync(
            $"/admin/tenants/{id}/branding", new { displayName = "Maquis", logoUrl = "https://cdn.example.com/a\0b.png" }));

        Assert.Equal(
            "https://cdn.example.com/a%00b.png",
            (await GetAsync(id)).GetProperty("branding").GetProperty("logoUrl").GetString());
    }

    [Fact]
    public async Task An_unknown_tenant_is_a_404_on_every_route()
    {
        var id = Guid.CreateVersion7();

        await AssertProblemAsync(
            await Client.GetAsync($"/admin/tenants/{id}"), HttpStatusCode.NotFound, "tenants.tenant.not_found");
        await AssertProblemAsync(
            await Client.PostAsync($"/admin/tenants/{id}/activate", null), HttpStatusCode.NotFound, "tenants.tenant.not_found");
        await AssertProblemAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/name", new { name = "Le Maquis" }),
            HttpStatusCode.NotFound,
            "tenants.tenant.not_found");
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_matches_no_route()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/admin/tenants/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task The_lifecycle_routes_drive_the_tenant_through_its_statuses()
    {
        var id = await CreateAsync();

        await AssertNoContentAsync(await Client.PostAsync($"/admin/tenants/{id}/activate", null));
        Assert.Equal("Active", (await GetAsync(id)).GetProperty("status").GetString());

        await AssertNoContentAsync(
            await Client.PostAsJsonAsync($"/admin/tenants/{id}/suspend", new { reason = "PaymentOverdue" }));
        var suspended = await GetAsync(id);
        Assert.Equal("Suspended", suspended.GetProperty("status").GetString());
        Assert.Equal("PaymentOverdue", suspended.GetProperty("suspensionReason").GetString());
        Assert.False(suspended.GetProperty("canAcceptBusinessWrites").GetBoolean());

        await AssertNoContentAsync(await Client.PostAsync($"/admin/tenants/{id}/reactivate", null));
        Assert.Equal("Active", (await GetAsync(id)).GetProperty("status").GetString());

        await AssertNoContentAsync(await Client.PostAsync($"/admin/tenants/{id}/close", null));
        Assert.Equal("Closed", (await GetAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_refused_transition_is_a_422_carrying_the_domain_code()
    {
        var id = await CreateAsync();
        await Client.PostAsync($"/admin/tenants/{id}/close", null);

        await AssertProblemAsync(
            await Client.PostAsync($"/admin/tenants/{id}/activate", null),
            HttpStatusCode.UnprocessableEntity,
            "tenants.tenant.closed");
        await AssertProblemAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/name", new { name = "Le Maquis" }),
            HttpStatusCode.UnprocessableEntity,
            "tenants.tenant.closed");
    }

    [Fact]
    public async Task Suspend_refuses_a_reason_that_is_not_a_known_value()
    {
        var id = await CreateAsync();

        await AssertProblemAsync(
            await Client.PostAsJsonAsync($"/admin/tenants/{id}/suspend", new { reason = 99 }),
            HttpStatusCode.UnprocessableEntity,
            "tenants.tenant.suspension_reason_invalid");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync($"/admin/tenants/{id}/suspend", new { reason = "Whatever" })).StatusCode);
    }

    [Fact]
    public async Task The_settings_routes_replace_each_setting()
    {
        var id = await CreateAsync();

        await AssertNoContentAsync(await Client.PutAsJsonAsync($"/admin/tenants/{id}/name", new { name = "Le Maquis" }));
        await AssertNoContentAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/default-currency", new { currencyCode = "EUR" }));
        await AssertNoContentAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/time-zone", new { timeZoneId = "Europe/Paris" }));
        await AssertNoContentAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/price-tax-mode", new { pricesIncludeTax = false }));
        await AssertNoContentAsync(await Client.PutAsJsonAsync($"/admin/tenants/{id}/branding", new
        {
            displayName = "Maquis", logoUrl = "https://cdn.example.com/logo.png", primaryColor = "#abc",
        }));

        var tenant = await GetAsync(id);
        Assert.Equal("Le Maquis", tenant.GetProperty("name").GetString());
        Assert.Equal("EUR", tenant.GetProperty("defaultCurrencyCode").GetString());
        Assert.Equal("Europe/Paris", tenant.GetProperty("timeZoneId").GetString());
        Assert.False(tenant.GetProperty("pricesIncludeTax").GetBoolean());
        var branding = tenant.GetProperty("branding");
        Assert.Equal("Maquis", branding.GetProperty("displayName").GetString());
        Assert.Equal("https://cdn.example.com/logo.png", branding.GetProperty("logoUrl").GetString());
        Assert.Equal("#AABBCC", branding.GetProperty("primaryColor").GetString());
    }

    [Fact]
    public async Task Branding_without_logo_or_color_removes_them()
    {
        var id = await CreateAsync();
        await Client.PutAsJsonAsync($"/admin/tenants/{id}/branding", new
        {
            displayName = "Maquis", logoUrl = "https://cdn.example.com/logo.png", primaryColor = "#abc",
        });

        await AssertNoContentAsync(await Client.PutAsJsonAsync($"/admin/tenants/{id}/branding", new { displayName = "Maquis" }));

        var branding = (await GetAsync(id)).GetProperty("branding");
        Assert.Equal(JsonValueKind.Null, branding.GetProperty("logoUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, branding.GetProperty("primaryColor").ValueKind);
    }

    [Fact]
    public async Task Invalid_settings_are_a_422_carrying_the_domain_code()
    {
        var id = await CreateAsync();

        await AssertProblemAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/branding", new { displayName = "Maquis", logoUrl = "logo.png" }),
            HttpStatusCode.UnprocessableEntity,
            "tenants.branding.logo_url_invalid");
        await AssertProblemAsync(
            await Client.PutAsJsonAsync($"/admin/tenants/{id}/time-zone", new { }),
            HttpStatusCode.UnprocessableEntity,
            "common.time_zone.empty");
    }

    [Fact]
    public async Task Price_tax_mode_without_the_field_is_a_400_and_changes_nothing()
    {
        var id = await CreateAsync();

        var response = await Client.PutAsJsonAsync($"/admin/tenants/{id}/price-tax-mode", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await GetAsync(id)).GetProperty("pricesIncludeTax").GetBoolean());
    }

    [Fact]
    public async Task The_currency_is_locked_once_the_catalogue_is_priced()
    {
        var id = await CreateAsync();
        api.HasPricedCatalog = true;
        try
        {
            await AssertProblemAsync(
                await Client.PutAsJsonAsync($"/admin/tenants/{id}/default-currency", new { currencyCode = "EUR" }),
                HttpStatusCode.UnprocessableEntity,
                "tenants.tenant.currency_locked_by_priced_catalog");
        }
        finally
        {
            api.HasPricedCatalog = false;
        }

        Assert.Equal("XOF", (await GetAsync(id)).GetProperty("defaultCurrencyCode").GetString());
    }

    [Fact]
    public async Task A_concurrent_update_is_a_409()
    {
        await AssertProblemAsync(
            await Client.GetAsync("/concurrent-update"), HttpStatusCode.Conflict, "tenants.tenant.concurrent_update");
    }

    [Fact]
    public async Task A_failed_authentication_is_a_401()
    {
        await AssertProblemAsync(
            await Client.GetAsync("/unauthenticated"), HttpStatusCode.Unauthorized, "test.unauthenticated");
    }

    private async Task<Guid> CreateAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/admin/tenants", new { name = "Chez Hector", defaultCurrencyCode = "XOF", timeZoneId = "Africa/Porto-Novo" });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private Task<JsonElement> GetAsync(Guid id) => Client.GetFromJsonAsync<JsonElement>($"/admin/tenants/{id}");

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertNoContentAsync(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
    }
}

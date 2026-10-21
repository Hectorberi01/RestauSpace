using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.Host.Tests;

public sealed class HostTests(HostFixture host) : IClassFixture<HostFixture>
{
    private static readonly object NewTenant =
        new { name = "Chez Hector", defaultCurrencyCode = "XOF", timeZoneId = "Africa/Porto-Novo" };

    [Fact]
    public async Task Health_is_open_to_everyone()
    {
        Assert.Equal(HttpStatusCode.OK, (await host.Anonymous().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Back_office_routes_refuse_a_caller_without_a_token()
    {
        var client = host.Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/admin/tenants", NewTenant)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/v1/admin/tenants/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/v1/admin/users/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_api_key_is_unauthorized_everywhere()
    {
        var client = host.Anonymous();
        client.DefaultRequestHeaders.Add("X-Api-Key", "srk_unknown-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/integrations/webhooks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Reviews_routes_are_mapped_and_need_a_selected_restaurant()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync("/v1/reviews/feedbacks")).StatusCode);
        var client = await host.SignedInAsync(HostFixture.NewEmail());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/reviews/feedbacks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/reviews/surveys", new { title = "Visite" })).StatusCode);
    }

    [Fact]
    public async Task Back_office_routes_refuse_an_ordinary_signed_in_user_who_still_reaches_its_own_account()
    {
        var client = await host.SignedInAsync(HostFixture.NewEmail());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/admin/tenants", NewTenant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/admin/tenants/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/admin/users/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/v1/admin/users/invitations", new { email = HostFixture.NewEmail() })).StatusCode);
    }

    [Fact]
    public async Task A_listed_admin_runs_a_tenant_through_the_whole_stack()
    {
        var client = await host.SignedInAsync(HostFixture.AdminEmail);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var created = await client.PostAsJsonAsync("/v1/admin/tenants", NewTenant);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location;

        // Nécessite le port du catalogue tarifé : l'hôte doit en avoir enregistré un.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"{location}/default-currency", new { currencyCode = "EUR" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"{location}/suspend", new { reason = "PaymentOverdue" })).StatusCode);

        var tenant = await client.GetFromJsonAsync<JsonElement>(location);
        Assert.Equal("Suspended", tenant.GetProperty("status").GetString());
        Assert.Equal("EUR", tenant.GetProperty("defaultCurrencyCode").GetString());
        // La vraie horloge : maintenant, et en UTC.
        var createdAt = tenant.GetProperty("createdAtUtc").GetDateTimeOffset();
        Assert.InRange(createdAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, createdAt.Offset);
    }

    [Fact]
    public async Task Creating_a_restaurant_is_written_to_the_audit_journal_through_the_outbox()
    {
        var client = await host.SignedInAsync(HostFixture.AdminEmail);
        var created = await client.PostAsJsonAsync("/v1/admin/tenants", NewTenant);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // L'outbox est relevée toutes les 2 s : on attend l'écriture au plus 20 s.
        JsonElement entries = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            entries = (await client.GetFromJsonAsync<JsonElement>($"/v1/admin/audit?tenantId={tenantId}&action=tenants.tenant.created")).GetProperty("items");
            if (entries.GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(500);
        }

        if (entries.GetArrayLength() == 0)
        {
            Assert.Fail("Aucune écriture d'audit. " + await host.OutboxStateAsync("tenants", tenantId));
        }

        var entry = Assert.Single(entries.EnumerateArray());
        Assert.Equal(tenantId.ToString(), entry.GetProperty("targetId").GetString());
        Assert.Equal("Chez Hector", entry.GetProperty("metadata").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_listed_admin_manages_accounts()
    {
        var admin = await host.SignedInAsync(HostFixture.AdminEmail);
        var invitedEmail = HostFixture.NewEmail();

        var invited = await admin.PostAsJsonAsync("/v1/admin/users/invitations", new { email = invitedEmail });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.Anonymous().PostAsJsonAsync(
                "/v1/auth/accept-invitation", new { token = host.LastTokenSentTo(invitedEmail), password = HostFixture.Password })).StatusCode);
        var user = await admin.GetFromJsonAsync<JsonElement>($"/v1/admin/users/{id}");
        Assert.Equal("Active", user.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Taking_a_listed_admin_address_without_verifying_it_gives_no_admin_right()
    {
        var client = await host.SignedInAsync(HostFixture.NewEmail());
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync(
                "/v1/me/email", new { newEmail = HostFixture.UnclaimedAdminEmail, currentPassword = HostFixture.Password })).StatusCode);

        // Un nouveau jeton porte désormais l'adresse admin... non vérifiée.
        var escalated = await host.SignedInAsync(HostFixture.UnclaimedAdminEmail);

        Assert.Equal(HttpStatusCode.OK, (await escalated.GetAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await escalated.PostAsJsonAsync("/v1/admin/tenants", NewTenant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await escalated.GetAsync($"/v1/admin/users/{Guid.CreateVersion7()}")).StatusCode);
    }

    [Fact]
    public async Task The_auth_routes_are_rate_limited_per_address_from_configuration()
    {
        using var limited = host.WithWebHostBuilder(
            builder => builder.UseSetting("Identity:RateLimiting:Auth:PermitLimit", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.PostAsJsonAsync(
                "/v1/auth/login", new { email = HostFixture.NewEmail(), password = "whatever" })).StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task A_disabled_admin_loses_the_back_office_at_once()
    {
        var admin = await host.SignedInAsync(HostFixture.AdminEmail);
        var colleagueEmail = HostFixture.NewEmail();
        var colleague = await host.SignedInAsync(colleagueEmail);
        var colleagueId = (await colleague.GetFromJsonAsync<JsonElement>("/v1/me")).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/v1/admin/users/{colleagueId}/disable", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await colleague.GetAsync("/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var client = await host.SignedInAsync(HostFixture.AdminEmail);

        var response = await client.GetAsync($"/v1/admin/tenants/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenants.tenant.not_found", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Identity:AccessTokens:SigningKey", "", "SigningKey' is not configured")]
    [InlineData("Identity:AccessTokens:SigningKey", "c2hvcnQ=", "at least 32 bytes")]
    [InlineData("Identity:AccessTokens:Issuer", "", "issuer and audience are required")]
    [InlineData("ConnectionStrings:SaasRestaurant", "", "Connection string")]
    public void The_host_refuses_to_start_in_production_with_a_missing_or_weak_setting(string key, string value, string reason)
    {
        using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SaasRestaurant", "Host=localhost;Database=unused");
            builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32]));
            // Les emails ne sont pas le sujet ici.
            builder.ConfigureTestServices(services => services.AddSingleton<IIdentityNotifier>(host));
            builder.UseSetting(key, value);
        });

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
        Assert.Contains(reason, exception.ToString());
    }

    [Fact]
    public void The_host_refuses_to_write_emailed_links_to_the_log_outside_development()
    {
        using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SaasRestaurant", "Host=localhost;Database=unused");
            builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Identity:Emails:WriteToLog", "true");
        });

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
        Assert.Contains("only allowed in Development", exception.ToString());
    }

    [Theory]
    [InlineData("https://app.example.com/verify?token={token}", null)]
    [InlineData("https://app.example.com/verify", "must be an absolute http(s) URL containing {token}")]
    [InlineData("", "must be an absolute http(s) URL containing {token}")]
    public async Task With_an_smtp_server_configured_the_host_starts_in_production_only_if_the_email_links_are_valid(
        string verifyLink, string? refusal)
    {
        using var configured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SaasRestaurant", "Host=localhost;Database=unused");
            builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Identity:Emails:Smtp:Host", "smtp.example.com");
            builder.UseSetting("Identity:Emails:Smtp:FromAddress", "no-reply@example.com");
            builder.UseSetting("Identity:Emails:Links:VerifyEmail", verifyLink);
            builder.UseSetting("Identity:Emails:Links:ResetPassword", "https://app.example.com/reset?token={token}");
            builder.UseSetting("Identity:Emails:Links:AcceptInvitation", "https://app.example.com/invite?token={token}");
            HostFixture.UseModuleSettings(builder);
        });

        if (refusal is null)
        {
            Assert.Equal(HttpStatusCode.OK, (await configured.CreateClient().GetAsync("/health")).StatusCode);
        }
        else
        {
            Assert.Contains(refusal, Assert.ThrowsAny<Exception>(() => configured.CreateClient()).ToString());
        }
    }

    [Fact]
    public void The_host_refuses_to_start_without_any_way_to_send_emails()
    {
        using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SaasRestaurant", "Host=localhost;Database=unused");
            builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32]));
            HostFixture.UseModuleSettings(builder);
        });

        var exception = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
        Assert.Contains("email sender", exception.ToString());
    }
}

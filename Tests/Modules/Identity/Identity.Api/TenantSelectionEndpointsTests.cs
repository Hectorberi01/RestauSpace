using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Identity.Api;

public sealed class TenantSelectionEndpointsTests(IdentityApi api) : IClassFixture<IdentityApi>
{
    private static string? TenantOf(string accessToken) =>
        new JsonWebToken(accessToken).Claims.FirstOrDefault(claim => claim.Type == IdentityClaims.TenantId)?.Value;

    [Fact]
    public async Task Selecting_a_tenant_needs_a_signed_in_user()
    {
        var response = await api.Anonymous().PostAsJsonAsync("/me/tenant", new { tenantId = Guid.NewGuid() });

        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_gets_a_token_acting_for_the_tenant_that_still_opens_its_session()
    {
        var (_, tokens, client) = await api.GivenSignedInUserAsync();
        var tenantId = Guid.NewGuid();
        ApiTenantMemberships.Grant(tokens.GetProperty("userId").GetGuid(), tenantId);

        var response = await client.PostAsJsonAsync("/me/tenant", new { tenantId });

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var selected = await response.ReadAsync();
        Assert.Equal(tenantId, selected.GetProperty("tenantId").GetGuid());
        Assert.Equal(tenantId.ToString(), TenantOf(selected.Text("accessToken")));
        Assert.Null(TenantOf(tokens.Text("accessToken")));
        await (await api.WithToken(selected.Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_tenant_the_user_is_not_a_member_of_is_refused()
    {
        var (_, _, client) = await api.GivenSignedInUserAsync();

        var response = await client.PostAsJsonAsync("/me/tenant", new { tenantId = Guid.NewGuid() });

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "identity.tenant.access_denied");
    }

    [Fact]
    public async Task Refresh_keeps_the_tenant_only_while_the_membership_lasts_and_a_refusal_keeps_the_refresh_token()
    {
        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        var userId = tokens.GetProperty("userId").GetGuid();
        var tenantId = Guid.NewGuid();
        ApiTenantMemberships.Grant(userId, tenantId);

        var refreshed = await api.Anonymous().PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = tokens.Text("refreshToken"), tenantId });
        await refreshed.AssertStatusAsync(HttpStatusCode.OK);
        var second = await refreshed.ReadAsync();
        Assert.Equal(tenantId.ToString(), TenantOf(second.Text("accessToken")));

        ApiTenantMemberships.Revoke(userId, tenantId);
        var refused = await api.Anonymous().PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = second.Text("refreshToken"), tenantId });
        await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "identity.tenant.access_denied");

        // Le refresh refusé n'a rien fait tourner : le même refresh token fonctionne toujours, sans tenant.
        var withoutTenant = await api.Anonymous().PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = second.Text("refreshToken") });
        await withoutTenant.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Null(TenantOf((await withoutTenant.ReadAsync()).Text("accessToken")));
    }
}

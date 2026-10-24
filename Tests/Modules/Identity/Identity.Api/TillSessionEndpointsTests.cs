using System.Net;
using System.Net.Http.Json;
using Identity.Application.Commands;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Identity.Api;

/// <summary>Session de caisse (PIN) : jeton marqué, sans refresh token, sans accès aux routes sensibles de /me.</summary>
public sealed class TillSessionEndpointsTests(IdentityApi api) : IClassFixture<IdentityApi>
{
    private async Task<(OperatorSessionDto Session, HttpClient Client, Guid Till, Guid Location)> GivenTillSessionAsync()
    {
        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        var userId = tokens.GetProperty("userId").GetGuid();
        var tenantId = Guid.NewGuid();
        var (till, location) = (Guid.NewGuid(), Guid.NewGuid());
        ApiTenantMemberships.Grant(userId, tenantId);
        var session = await api.StartTillSessionAsync(userId, tenantId, till, location);
        return (session, api.WithToken(session.AccessToken), till, location);
    }

    [Fact]
    public async Task The_access_token_names_the_till_and_its_location_and_lasts_the_till_session()
    {
        var (session, client, till, location) = await GivenTillSessionAsync();

        var token = new JsonWebToken(session.AccessToken);
        Assert.Equal(till.ToString(), token.Claims.Single(claim => claim.Type == TillClaims.TillId).Value);
        Assert.Equal(location.ToString(), token.Claims.Single(claim => claim.Type == TillClaims.LocationId).Value);
        Assert.Equal(session.TenantId.ToString(), token.Claims.Single(claim => claim.Type == IdentityClaims.TenantId).Value);
        Assert.True(session.AccessTokenExpiresAtUtc > api.Clock.UtcNow.AddHours(11));
        await (await client.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sensitive_me_routes_and_choosing_another_restaurant_are_refused()
    {
        var (_, client, _, _) = await GivenTillSessionAsync();
        const string Forbidden = "common.till_session.forbidden";

        await (await client.PutAsJsonAsync("/me/password", new { currentPassword = IdentityApi.Password, newPassword = "a brand new password" }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.PutAsJsonAsync("/me/email", new { newEmail = IdentityApi.NewEmail(), currentPassword = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.PostAsJsonAsync("/me/two-factor/setup", new { password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.PostAsJsonAsync("/me/erase", new { currentPassword = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.PostAsJsonAsync("/me/tenant", new { tenantId = Guid.NewGuid() }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.GetAsync("/me/sessions")).AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
        await (await client.DeleteAsync("/me/sessions")).AssertProblemAsync(HttpStatusCode.Forbidden, Forbidden);
    }

    [Fact]
    public async Task A_till_session_ends_only_itself()
    {
        var (session, client, _, _) = await GivenTillSessionAsync();

        await (await client.DeleteAsync($"/me/sessions/{Guid.CreateVersion7()}")).AssertProblemAsync(HttpStatusCode.Forbidden, "common.till_session.forbidden");
        await (await client.DeleteAsync($"/me/sessions/{session.SessionId}")).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await client.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }
}

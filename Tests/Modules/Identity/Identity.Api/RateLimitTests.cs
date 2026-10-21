using System.Net;
using System.Net.Http.Json;

namespace Identity.Api;

public sealed class RateLimitedIdentityApi : IdentityApi
{
    public const int Limit = 5;

    protected override int AuthPermitLimit => Limit;
}

public sealed class RateLimitTests(RateLimitedIdentityApi api) : IClassFixture<RateLimitedIdentityApi>
{
    [Fact]
    public async Task One_address_gets_a_limited_number_of_auth_requests_per_window_and_other_routes_are_not_affected()
    {
        var client = api.Anonymous();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitedIdentityApi.Limit + 3; i++)
        {
            var route = i % 2 == 0 ? "/auth/login" : "/auth/forgot-password";
            statuses.Add((await client.PostAsJsonAsync(route, new { email = IdentityApi.NewEmail(), password = "whatever" })).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(RateLimitedIdentityApi.Limit));
        Assert.All(statuses.Skip(RateLimitedIdentityApi.Limit), status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me")).StatusCode);
    }
}

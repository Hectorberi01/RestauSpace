using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.Api;

public sealed class AuthEndpointsTests(IdentityApi api) : IClassFixture<IdentityApi>
{
    private HttpClient Client => api.Anonymous();

    [Fact]
    public async Task Register_verify_and_login_give_tokens_that_open_the_me_routes()
    {
        var email = IdentityApi.NewEmail();

        var registered = await Client.PostAsJsonAsync("/auth/register", new { email = email.ToUpperInvariant(), password = IdentityApi.Password });
        await registered.AssertStatusAsync(HttpStatusCode.Created);
        Assert.Null(registered.Headers.Location);
        var id = (await registered.ReadAsync()).GetProperty("id").GetGuid();
        Assert.Equal(["verification"], api.KindsSentTo(email));

        await (await Client.PostAsJsonAsync("/auth/login", new { email, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.email_not_verified");
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token = api.LastTokenSentTo(email), password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        var tokens = await api.LoginAsync(email);
        Assert.Equal(id, tokens.GetProperty("userId").GetGuid());
        Assert.InRange(
            tokens.GetProperty("accessTokenExpiresAtUtc").GetDateTimeOffset(),
            api.Clock.UtcNow.AddMinutes(14), api.Clock.UtcNow.AddMinutes(16));

        var me = await api.WithToken(tokens.Text("accessToken")).GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(id, me.GetProperty("id").GetGuid());
        Assert.Equal(email, me.Text("email"));
        Assert.Equal("Active", me.Text("status"));
        Assert.True(me.GetProperty("isEmailVerified").GetBoolean());
        // Rien de secret n'est jamais renvoyé.
        Assert.DoesNotContain("hash", me.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-an-email", IdentityApi.Password, "common.email.invalid")]
    [InlineData("valid@example.com", "short", "identity.password.too_short")]
    [InlineData("valid@example.com", null, "identity.password.empty")]
    public async Task Register_with_invalid_data_is_a_422(string email, string? password, string code)
    {
        await (await Client.PostAsJsonAsync("/auth/register", new { email, password }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, code);
    }

    [Fact]
    public async Task Register_twice_with_the_same_email_is_refused()
    {
        var email = IdentityApi.NewEmail();
        await api.GivenActiveUserAsync(email);

        await (await Client.PostAsJsonAsync("/auth/register", new { email, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "identity.user.email_already_used");
    }

    [Fact]
    public async Task Login_fails_the_same_way_for_a_wrong_password_and_an_unknown_account()
    {
        var email = IdentityApi.NewEmail();
        await api.GivenActiveUserAsync(email);

        await (await Client.PostAsJsonAsync("/auth/login", new { email, password = "wrong password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/login", new { email = IdentityApi.NewEmail(), password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/login", new { }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_replaying_the_old_one_signs_out_everywhere()
    {
        var (email, first, _) = await api.GivenSignedInUserAsync();
        var otherDevice = await api.LoginAsync(email);

        var refreshed = await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.Text("refreshToken") });
        await refreshed.AssertStatusAsync(HttpStatusCode.OK);
        var second = await refreshed.ReadAsync();
        Assert.NotEqual(first.Text("refreshToken"), second.Text("refreshToken"));
        await (await api.WithToken(second.Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);

        await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.refresh_token_replayed");
        foreach (var token in new[] { second.Text("refreshToken"), otherDevice.Text("refreshToken") })
        {
            await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = token }))
                .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
        }
    }

    [Theory]
    [InlineData("never-issued")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Refresh_with_an_unknown_token_is_a_401(string? refreshToken)
    {
        await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Logout_ends_the_session_and_can_be_repeated()
    {
        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        var refreshToken = tokens.Text("refreshToken");

        await (await Client.PostAsJsonAsync("/auth/logout", new { refreshToken })).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await Client.PostAsJsonAsync("/auth/logout", new { refreshToken })).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await Client.PostAsJsonAsync("/auth/logout", new { refreshToken = "never-issued" })).AssertStatusAsync(HttpStatusCode.NoContent);

        await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
    }

    [Fact]
    public async Task Forgot_password_answers_202_whether_the_account_exists_or_not_and_the_link_resets_once()
    {
        var (email, before, _) = await api.GivenSignedInUserAsync();
        var unknown = IdentityApi.NewEmail();

        await (await Client.PostAsJsonAsync("/auth/forgot-password", new { email })).AssertStatusAsync(HttpStatusCode.Accepted);
        await (await Client.PostAsJsonAsync("/auth/forgot-password", new { email = unknown })).AssertStatusAsync(HttpStatusCode.Accepted);
        Assert.Empty(api.KindsSentTo(unknown));
        var token = api.LastTokenSentTo(email);

        await (await Client.PostAsJsonAsync("/auth/reset-password", new { token, newPassword = "short" }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.password.too_short");
        await (await Client.PostAsJsonAsync("/auth/reset-password", new { token, newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        await (await Client.PostAsJsonAsync("/auth/reset-password", new { token, newPassword = "yet another password" }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.token.invalid");

        await (await Client.PostAsJsonAsync("/auth/login", new { email, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = before.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
        Assert.NotNull((await api.LoginAsync(email, "a brand new password")).Text("accessToken"));
    }

    [Fact]
    public async Task Resend_verification_answers_202_and_sends_a_working_link_only_to_an_unverified_account()
    {
        var pending = IdentityApi.NewEmail();
        await Client.PostAsJsonAsync("/auth/register", new { email = pending, password = IdentityApi.Password });
        var firstLink = api.LastTokenSentTo(pending);
        var (verified, _, _) = await api.GivenSignedInUserAsync();

        await (await Client.PostAsJsonAsync("/auth/resend-verification", new { email = pending })).AssertStatusAsync(HttpStatusCode.Accepted);
        await (await Client.PostAsJsonAsync("/auth/resend-verification", new { email = verified })).AssertStatusAsync(HttpStatusCode.Accepted);

        Assert.Equal(["verification"], api.KindsSentTo(verified));
        Assert.Equal(["verification", "verification"], api.KindsSentTo(pending));
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token = firstLink, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.token.invalid");
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token = api.LastTokenSentTo(pending), password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Simultaneous_requests_for_a_link_all_answer_202_for_an_existing_account_as_for_an_unknown_one()
    {
        var (verified, _, _) = await api.GivenSignedInUserAsync();
        var pending = IdentityApi.NewEmail();
        await Client.PostAsJsonAsync("/auth/register", new { email = pending, password = IdentityApi.Password });

        var forgot = await Task.WhenAll(Enumerable.Range(0, 12).Select(
            _ => Client.PostAsJsonAsync("/auth/forgot-password", new { email = verified })));
        var resend = await Task.WhenAll(Enumerable.Range(0, 12).Select(
            _ => Client.PostAsJsonAsync("/auth/resend-verification", new { email = pending })));

        Assert.All(forgot.Concat(resend), response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        // Le dernier lien envoyé est valide.
        await (await Client.PostAsJsonAsync(
                "/auth/reset-password", new { token = api.LastTokenSentTo(verified), newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token = api.LastTokenSentTo(pending), password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Five_devices_signing_in_at_the_same_instant_all_succeed()
    {
        var email = IdentityApi.NewEmail();
        await api.GivenActiveUserAsync(email);

        var logins = await Task.WhenAll(Enumerable.Range(0, 5).Select(
            _ => Client.PostAsJsonAsync("/auth/login", new { email, password = IdentityApi.Password })));

        Assert.All(logins, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var me = api.WithToken((await logins[0].ReadAsync()).Text("accessToken"));
        Assert.Equal(5, (await me.GetFromJsonAsync<JsonElement>("/me/sessions")).GetArrayLength());
    }

    [Fact]
    public async Task One_refresh_token_sent_twice_at_once_gives_one_new_pair_a_conflict_and_nobody_is_signed_out()
    {
        var (email, tokens, _) = await api.GivenSignedInUserAsync();
        var otherDevice = await api.LoginAsync(email);

        var refreshes = await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") })));

        var winner = Assert.Single(refreshes, response => response.StatusCode == HttpStatusCode.OK);
        Assert.All(refreshes.Where(response => response != winner), response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        // Pas traité comme un vol : la nouvelle paire et l'autre appareil fonctionnent toujours.
        await (await api.WithToken((await winner.ReadAsync()).Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);
        await (await api.WithToken(otherDevice.Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);

        // Plus tard, l'ancien jeton est un rejeu comme un autre.
        await (await Client.PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.refresh_token_replayed");
    }

    [Fact]
    public async Task Verifying_an_email_requires_the_password_and_a_wrong_one_leaves_the_link_usable()
    {
        var email = IdentityApi.NewEmail();
        await Client.PostAsJsonAsync("/auth/register", new { email, password = IdentityApi.Password });
        var token = api.LastTokenSentTo(email);

        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token, password = "wrong password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token, password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_account_someone_else_registered_on_my_address_cannot_be_activated_with_their_password()
    {
        var victim = IdentityApi.NewEmail();
        await Client.PostAsJsonAsync("/auth/register", new { email = victim, password = "the squatter's password" });

        // La victime possède la boîte mail, donc le lien... mais pas le mot de passe du squatteur.
        await (await Client.PostAsJsonAsync("/auth/verify-email", new { token = api.LastTokenSentTo(victim), password = "my own password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await Client.PostAsJsonAsync("/auth/login", new { email = victim, password = "the squatter's password" }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.email_not_verified");

        // « Mot de passe oublié » rend le compte au propriétaire de la boîte mail et exclut le squatteur.
        await (await Client.PostAsJsonAsync("/auth/forgot-password", new { email = victim })).AssertStatusAsync(HttpStatusCode.Accepted);
        await (await Client.PostAsJsonAsync("/auth/reset-password", new { token = api.LastTokenSentTo(victim), newPassword = "my own password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.NotNull((await api.LoginAsync(victim, "my own password")).Text("accessToken"));
        await (await Client.PostAsJsonAsync("/auth/login", new { email = victim, password = "the squatter's password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
    }

    [Fact]
    public async Task Too_many_wrong_passwords_block_the_address_for_a_while_even_with_the_right_password()
    {
        var email = IdentityApi.NewEmail();
        await api.GivenActiveUserAsync(email);
        var unknown = IdentityApi.NewEmail();

        foreach (var account in new[] { email, unknown })
        {
            for (var i = 0; i < 10; i++)
            {
                await (await Client.PostAsJsonAsync("/auth/login", new { email = account, password = "wrong password" }))
                    .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
            }

            // Même réponse pour un compte inexistant : le blocage ne révèle rien.
            var blocked = await Client.PostAsJsonAsync("/auth/login", new { email = account, password = IdentityApi.Password });
            await blocked.AssertProblemAsync(HttpStatusCode.TooManyRequests, "identity.auth.too_many_attempts");
            Assert.InRange(blocked.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        }

        // Le propriétaire n'est pas enfermé dehors : une réinitialisation lève le blocage.
        await Client.PostAsJsonAsync("/auth/forgot-password", new { email });
        await (await Client.PostAsJsonAsync("/auth/reset-password", new { token = api.LastTokenSentTo(email), newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.NotNull((await api.LoginAsync(email, "a brand new password")).Text("accessToken"));
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    [InlineData("/auth/refresh")]
    public async Task A_missing_or_unreadable_body_is_a_400(string route)
    {
        await (await Client.PostAsync(route, null)).AssertStatusAsync(HttpStatusCode.BadRequest);
        await (await Client.PostAsync(route, new StringContent("{oops", System.Text.Encoding.UTF8, "application/json")))
            .AssertStatusAsync(HttpStatusCode.BadRequest);
    }
}

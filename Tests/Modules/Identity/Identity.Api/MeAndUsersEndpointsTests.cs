using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.Api;

public sealed class MeAndUsersEndpointsTests(IdentityApi api) : IClassFixture<IdentityApi>
{
    [Theory]
    [InlineData("GET", "/me")]
    [InlineData("GET", "/me/sessions")]
    [InlineData("DELETE", "/me/sessions")]
    [InlineData("GET", "/admin/users/0199a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/admin/users/0199a000-0000-7000-8000-000000000001/disable")]
    public async Task Protected_routes_refuse_a_caller_without_a_valid_token(string method, string route)
    {
        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        var tampered = tokens.Text("accessToken")[..^3] + "abc";

        await (await api.Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), route)))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await api.WithToken(tampered).SendAsync(new HttpRequestMessage(new HttpMethod(method), route)))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await api.WithToken("not-a-jwt").SendAsync(new HttpRequestMessage(new HttpMethod(method), route)))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_access_token_is_refused()
    {
        var email = IdentityApi.NewEmail();
        await api.GivenActiveUserAsync(email);
        // Émis il y a une heure selon l'horloge du module : ses 15 minutes sont bien expirées pour le vrai validateur.
        api.Clock.Advance(TimeSpan.FromHours(-1));
        JsonElement tokens;
        try
        {
            tokens = await api.LoginAsync(email);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromHours(1));
        }

        await (await api.WithToken(tokens.Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_access_token_stops_working_as_soon_as_its_session_is_signed_out()
    {
        var (email, tokens, client) = await api.GivenSignedInUserAsync();
        var otherDevice = api.WithToken((await api.LoginAsync(email)).Text("accessToken"));
        await (await client.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);

        await (await api.Anonymous().PostAsJsonAsync("/auth/logout", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        await (await client.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await otherDevice.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);

        await (await otherDevice.DeleteAsync("/me/sessions")).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await otherDevice.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_access_token_survives_the_refresh_that_follows_it_but_not_the_next_one()
    {
        var (email, first, _) = await api.GivenSignedInUserAsync();
        var beforeRefresh = api.WithToken(first.Text("accessToken"));

        var second = await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = first.Text("refreshToken") })).ReadAsync();
        await (await beforeRefresh.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);

        // Changer le mot de passe depuis ce token un peu plus ancien conserve la session qui a remplacé la sienne.
        await (await beforeRefresh.PutAsJsonAsync("/me/password", new { currentPassword = IdentityApi.Password, newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        var third = await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = second.Text("refreshToken") });
        await third.AssertStatusAsync(HttpStatusCode.OK);

        await (await beforeRefresh.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await api.WithToken((await third.ReadAsync()).Text("accessToken")).GetAsync("/me")).AssertStatusAsync(HttpStatusCode.OK);
        Assert.NotNull((await api.LoginAsync(email, "a brand new password")).Text("accessToken"));
    }

    [Fact]
    public async Task Locking_or_disabling_an_account_cuts_its_access_tokens_at_once()
    {
        var admin = await AdminAsync();
        var (_, lockedTokens, locked) = await api.GivenSignedInUserAsync();
        var (_, disabledTokens, disabled) = await api.GivenSignedInUserAsync();

        await (await admin.PostAsync($"/admin/users/{lockedTokens.GetProperty("userId").GetGuid()}/lock", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await admin.PostAsync($"/admin/users/{disabledTokens.GetProperty("userId").GetGuid()}/disable", null)).AssertStatusAsync(HttpStatusCode.NoContent);

        await (await locked.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await disabled.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await disabled.PutAsJsonAsync("/me/password", new { currentPassword = IdentityApi.Password, newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Users_are_searched_by_email_and_only_a_pending_account_gets_a_new_invitation()
    {
        var admin = await AdminAsync();
        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        var invited = $"invite-{Guid.NewGuid():N}@example.com";
        var invitedId = (await (await admin.PostAsJsonAsync("/admin/users/invitations", new { email = invited })).ReadAsync()).GetProperty("id").GetGuid();

        var found = await admin.GetFromJsonAsync<JsonElement>($"/admin/users?search={Uri.EscapeDataString(invited[..14])}");
        Assert.Equal(invitedId, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        await (await admin.PostAsync($"/admin/users/{invitedId}/invitation", null)).AssertStatusAsync(HttpStatusCode.Accepted);
        await (await admin.PostAsync($"/admin/users/{tokens.GetProperty("userId").GetGuid()}/invitation", null))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.not_invitable");
    }

    [Fact]
    public async Task My_sessions_are_listed_with_the_current_one_and_can_be_ended_one_by_one_or_all()
    {
        var (email, tokens, client) = await api.GivenSignedInUserAsync();
        var other = await api.LoginAsync(email);
        var third = await api.LoginAsync(email);

        var sessions = await client.GetFromJsonAsync<JsonElement>("/me/sessions");
        Assert.Equal(3, sessions.GetArrayLength());
        var current = Assert.Single(sessions.EnumerateArray(), session => session.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(tokens.GetProperty("sessionId").GetGuid(), current.GetProperty("id").GetGuid());

        await (await client.DeleteAsync($"/me/sessions/{other.GetProperty("sessionId").GetGuid()}")).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = other.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
        await (await client.DeleteAsync($"/me/sessions/{Guid.CreateVersion7()}"))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.session.not_found");

        await (await client.DeleteAsync("/me/sessions")).AssertStatusAsync(HttpStatusCode.NoContent);
        // Cette session elle-même a disparu aussi : son access token n'ouvre plus rien.
        await (await client.GetAsync("/me/sessions")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = third.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
    }

    [Fact]
    public async Task A_user_cannot_end_the_session_of_someone_else()
    {
        var (_, victim, _) = await api.GivenSignedInUserAsync();
        var (_, _, attacker) = await api.GivenSignedInUserAsync();

        await (await attacker.DeleteAsync($"/me/sessions/{victim.GetProperty("sessionId").GetGuid()}"))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.session.not_found");

        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = victim.Text("refreshToken") }))
            .AssertStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_my_password_needs_the_current_one_and_keeps_only_this_session()
    {
        var (email, tokens, client) = await api.GivenSignedInUserAsync();
        var other = await api.LoginAsync(email);

        await (await client.PutAsJsonAsync("/me/password", new { currentPassword = "wrong password", newPassword = "a brand new password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await client.PutAsJsonAsync("/me/password", new { currentPassword = IdentityApi.Password, newPassword = "a brand new password" }))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = other.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertStatusAsync(HttpStatusCode.OK);
        Assert.NotNull((await api.LoginAsync(email, "a brand new password")).Text("accessToken"));
    }

    [Fact]
    public async Task Changing_my_email_needs_my_password_and_the_new_address_must_be_verified()
    {
        var (_, _, client) = await api.GivenSignedInUserAsync();
        var newEmail = IdentityApi.NewEmail();

        await (await client.PutAsJsonAsync("/me/email", new { newEmail }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await client.PutAsJsonAsync("/me/email", new { newEmail, currentPassword = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal(newEmail, me.Text("email"));
        Assert.False(me.GetProperty("isEmailVerified").GetBoolean());
        await (await api.Anonymous().PostAsJsonAsync("/auth/verify-email", new { token = api.LastTokenSentTo(newEmail), password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/me")).GetProperty("isEmailVerified").GetBoolean());
    }

    [Fact]
    public async Task Erasing_my_account_needs_my_password_cuts_everything_and_frees_my_address()
    {
        var (email, tokens, client) = await api.GivenSignedInUserAsync();

        await (await client.PostAsJsonAsync("/me/erase", new { currentPassword = "wrong password" }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");
        await (await client.PostAsJsonAsync("/me/erase", new { currentPassword = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        await (await client.GetAsync("/me")).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_refresh_token");
        await (await api.Anonymous().PostAsJsonAsync("/auth/login", new { email, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.auth.invalid_credentials");

        await api.GivenActiveUserAsync(email);
        Assert.NotEqual(tokens.GetProperty("userId").GetGuid(), (await api.LoginAsync(email)).GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task The_back_office_erases_an_account_which_then_shows_no_personal_data()
    {
        var admin = await AdminAsync();
        var (email, tokens, _) = await api.GivenSignedInUserAsync();
        var id = tokens.GetProperty("userId").GetGuid();

        await (await admin.DeleteAsync($"/admin/users/{id}")).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await admin.DeleteAsync($"/admin/users/{id}")).AssertStatusAsync(HttpStatusCode.NoContent);

        var erased = await admin.GetFromJsonAsync<JsonElement>($"/admin/users/{id}");
        Assert.Equal("Disabled", erased.Text("status"));
        Assert.DoesNotContain(email, erased.GetRawText());
        Assert.NotEqual(JsonValueKind.Null, erased.GetProperty("erasedAtUtc").ValueKind);
        Assert.False(erased.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task The_users_routes_are_for_the_back_office_only()
    {
        var (_, _, user) = await api.GivenSignedInUserAsync();
        var target = await api.GivenActiveUserAsync(IdentityApi.NewEmail());

        await (await user.GetAsync($"/admin/users/{target}")).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await user.PostAsync($"/admin/users/{target}/disable", null)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await user.PostAsJsonAsync("/admin/users/invitations", new { email = IdentityApi.NewEmail() })).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await user.PutAsJsonAsync($"/admin/users/{target}/email", new { newEmail = IdentityApi.NewEmail() })).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await user.DeleteAsync($"/admin/users/{target}/sessions")).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await user.DeleteAsync($"/admin/users/{target}")).AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_back_office_invites_locks_unlocks_and_disables_accounts()
    {
        var admin = await AdminAsync();
        var invitedEmail = IdentityApi.NewEmail();

        var invited = await admin.PostAsJsonAsync("/admin/users/invitations", new { email = invitedEmail });
        await invited.AssertStatusAsync(HttpStatusCode.Created);
        var id = (await invited.ReadAsync()).GetProperty("id").GetGuid();
        Assert.False((await admin.GetFromJsonAsync<JsonElement>($"/admin/users/{id}")).GetProperty("hasPassword").GetBoolean());

        await (await api.Anonymous().PostAsJsonAsync(
                "/auth/accept-invitation", new { token = api.LastTokenSentTo(invitedEmail), password = IdentityApi.Password }))
            .AssertStatusAsync(HttpStatusCode.NoContent);
        var tokens = await api.LoginAsync(invitedEmail);

        await (await admin.PostAsync($"/admin/users/{id}/lock", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await api.Anonymous().PostAsJsonAsync("/auth/login", new { email = invitedEmail, password = IdentityApi.Password }))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.locked");
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.user.locked");

        await (await admin.PostAsync($"/admin/users/{id}/unlock", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal("Active", (await admin.GetFromJsonAsync<JsonElement>($"/admin/users/{id}")).Text("status"));
        await (await admin.PostAsync($"/admin/users/{id}/unlock", null))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "identity.user.not_locked");

        await (await admin.PostAsync($"/admin/users/{id}/disable", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal("Disabled", (await admin.GetFromJsonAsync<JsonElement>($"/admin/users/{id}")).Text("status"));
        await (await admin.GetAsync($"/admin/users/{Guid.CreateVersion7()}"))
            .AssertProblemAsync(HttpStatusCode.NotFound, "identity.user.not_found");
    }

    [Fact]
    public async Task The_back_office_corrects_an_invited_address_and_signs_a_user_out_everywhere()
    {
        var admin = await AdminAsync();
        var typo = IdentityApi.NewEmail();
        var corrected = IdentityApi.NewEmail();
        var id = (await (await admin.PostAsJsonAsync("/admin/users/invitations", new { email = typo })).ReadAsync()).GetProperty("id").GetGuid();

        await (await admin.PutAsJsonAsync($"/admin/users/{id}/email", new { newEmail = corrected })).AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.Equal(["invitation"], api.KindsSentTo(corrected));

        var (_, tokens, _) = await api.GivenSignedInUserAsync();
        await (await admin.DeleteAsync($"/admin/users/{tokens.GetProperty("userId").GetGuid()}/sessions")).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await api.Anonymous().PostAsJsonAsync("/auth/refresh", new { refreshToken = tokens.Text("refreshToken") }))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "identity.session.revoked");
    }

    private async Task<HttpClient> AdminAsync()
    {
        var login = await api.Anonymous().PostAsJsonAsync("/auth/login", new { email = IdentityApi.AdminEmail, password = IdentityApi.Password });
        if (!login.IsSuccessStatusCode)
        {
            await api.GivenActiveUserAsync(IdentityApi.AdminEmail);
            login = await api.Anonymous().PostAsJsonAsync("/auth/login", new { email = IdentityApi.AdminEmail, password = IdentityApi.Password });
        }

        return api.WithToken((await login.ReadAsync()).Text("accessToken"));
    }
}

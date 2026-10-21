using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Un tenant n'entre dans un access token que via une appartenance vérifiée (sélection et chaque refresh).</summary>
public sealed class TenantSelectionTests
{
    private readonly IdentityFixture _fixture = new();
    private readonly TenantId _restaurant = TenantId.New();

    private async Task<(User User, AuthTokensDto Tokens)> GivenSignedInMemberAsync()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var tokens = await _fixture.GivenSessionAsync();
        _fixture.Memberships.Add((user.Id, _restaurant));
        return (user, tokens);
    }

    [Fact]
    public async Task A_member_gets_an_access_token_acting_for_the_tenant_on_the_same_session()
    {
        var (user, tokens) = await GivenSignedInMemberAsync();
        var savesBefore = _fixture.SaveCount;

        var selected = await _fixture.SelectTenant.HandleAsync(
            new SelectTenantCommand(user.Id.Value, tokens.SessionId, _restaurant.Value));

        Assert.Equal(_restaurant.Value, selected.TenantId);
        Assert.Equal($"access.{user.Id}.{tokens.SessionId}.tenant.{_restaurant}", selected.AccessToken);
        Assert.Equal(_fixture.Clock.UtcNow.AddMinutes(15), selected.AccessTokenExpiresAtUtc);
        // Rien n'est écrit : le token constitue à lui seul la sélection.
        Assert.Equal(savesBefore, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_is_refused_with_the_same_answer_as_for_an_unknown_tenant()
    {
        var (user, tokens) = await GivenSignedInMemberAsync();

        var foreign = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.SelectTenant.HandleAsync(
            new SelectTenantCommand(user.Id.Value, tokens.SessionId, TenantId.New().Value)));
        var empty = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.SelectTenant.HandleAsync(
            new SelectTenantCommand(user.Id.Value, tokens.SessionId, Guid.Empty)));

        Assert.Equal(IdentityApplicationErrors.TenantAccessDenied, foreign.Error);
        Assert.Equal(IdentityApplicationErrors.TenantAccessDenied, empty.Error);
    }

    [Fact]
    public async Task Selecting_needs_a_session_and_a_known_user()
    {
        var (user, _) = await GivenSignedInMemberAsync();

        await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.SelectTenant.HandleAsync(
            new SelectTenantCommand(user.Id.Value, null, _restaurant.Value)));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.SelectTenant.HandleAsync(
            new SelectTenantCommand(Guid.NewGuid(), Guid.NewGuid(), _restaurant.Value)));
    }

    [Fact]
    public async Task Refresh_keeps_acting_for_the_tenant_while_the_user_is_a_member()
    {
        var (user, tokens) = await GivenSignedInMemberAsync();

        var refreshed = await _fixture.Refresh.HandleAsync(
            new RefreshSessionCommand(tokens.RefreshToken, TenantId: _restaurant.Value));

        Assert.EndsWith($".tenant.{_restaurant}", refreshed.AccessToken, StringComparison.Ordinal);
        Assert.Equal(refreshed.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
    }

    [Fact]
    public async Task Refresh_without_a_tenant_gives_a_token_acting_for_none()
    {
        var (_, tokens) = await GivenSignedInMemberAsync();

        var refreshed = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken));

        Assert.DoesNotContain(".tenant.", refreshed.AccessToken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_for_a_tenant_the_user_left_is_refused_and_saves_nothing()
    {
        var (user, tokens) = await GivenSignedInMemberAsync();
        _fixture.Memberships.Remove((user.Id, _restaurant));
        var savesBefore = _fixture.SaveCount;

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Refresh.HandleAsync(
            new RefreshSessionCommand(tokens.RefreshToken, TenantId: _restaurant.Value)));

        Assert.Equal(IdentityApplicationErrors.TenantAccessDenied, refused.Error);
        // Non sauvegardé : avec une vraie base, la rotation est abandonnée et le token présenté reste valide.
        Assert.Equal(savesBefore, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_replayed_token_is_detected_even_when_it_asks_for_a_tenant_it_cannot_use()
    {
        var (user, stolen) = await GivenSignedInMemberAsync();
        await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(stolen.RefreshToken));
        _fixture.Clock.Advance(RefreshSessionHandler.ConcurrentRefreshWindow + TimeSpan.FromSeconds(1));

        var replay = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.Refresh.HandleAsync(
            new RefreshSessionCommand(stolen.RefreshToken, TenantId: TenantId.New().Value)));

        Assert.Equal(UserErrors.RefreshTokenReplayed, replay.Error);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
    }
}

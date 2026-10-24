using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Famille de refresh tokens : tout ancien jeton révoque tout, et une famille a une durée maximale.</summary>
public sealed class SessionFamilyTests
{
    private readonly IdentityFixture _fixture = new();

    private async Task<AuthTokensDto> RefreshAsync(string token, TimeSpan? after = null)
    {
        _fixture.Clock.Advance(after ?? TimeSpan.FromMinutes(15));
        return await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(token));
    }

    [Fact]
    public async Task Renewed_tokens_carry_the_first_token_of_their_family()
    {
        await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();

        var second = await RefreshAsync(first.RefreshToken);
        var third = await RefreshAsync(second.RefreshToken);

        Assert.Null(RefreshTokenFamily.AnchorOf(first.RefreshToken));
        Assert.Equal(first.RefreshToken, RefreshTokenFamily.AnchorOf(second.RefreshToken));
        Assert.Equal(first.RefreshToken, RefreshTokenFamily.AnchorOf(third.RefreshToken));
    }

    [Fact]
    public async Task Reusing_a_token_two_generations_old_revokes_the_whole_family_and_every_session()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();
        var second = await RefreshAsync(first.RefreshToken);
        var third = await RefreshAsync(second.RefreshToken);
        var fourth = await RefreshAsync(third.RefreshToken);
        var otherDevice = await _fixture.GivenSessionAsync();
        // La génération 2 n'est plus en base : seule l'ancre la reconnaît.
        Assert.DoesNotContain(user.Sessions, session => session.Id.Value == second.SessionId);
        user.ClearDomainEvents();
        var saves = _fixture.SaveCount;

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(second.RefreshToken, TimeSpan.FromMinutes(1)));

        Assert.Equal(UserErrors.RefreshTokenReplayed.Code, exception.Code);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
        Assert.Equal(saves + 1, _fixture.SaveCount);
        Assert.Equal(first.SessionId, DomainAssert.Raised<RefreshTokenReplayDetected>(user).ReplayedSessionId.Value);
        foreach (var token in new[] { fourth.RefreshToken, otherDevice.RefreshToken })
        {
            var refused = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(token));
            Assert.Equal(UserErrors.SessionRevoked.Code, refused.Code);
        }
    }

    [Fact]
    public async Task The_first_token_is_still_recognised_after_many_rotations()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();
        var current = first;
        for (var i = 0; i < 50; i++)
        {
            current = await RefreshAsync(current.RefreshToken);
        }

        // Ancre, session remplacée et session courante : pas plus.
        Assert.Equal(3, user.Sessions.Count);
        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(first.RefreshToken));

        Assert.Equal(UserErrors.RefreshTokenReplayed.Code, exception.Code);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task An_old_token_of_another_family_is_unknown_and_revokes_nothing()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var current = await _fixture.GivenSessionAsync();

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand("never-issued.forged")));

        Assert.Equal(IdentityApplicationErrors.InvalidRefreshToken.Code, exception.Code);
        Assert.Contains(user.ActiveSessionsAt(_fixture.Clock.UtcNow), session => session.Id.Value == current.SessionId);
    }

    [Fact]
    public async Task A_family_never_outlives_its_maximum_lifetime_however_often_it_is_renewed()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var signedInAt = _fixture.Clock.UtcNow;
        var current = await _fixture.GivenSessionAsync();
        var familyEnd = signedInAt.Add(_fixture.Settings.SessionFamilyLifetime.Duration);

        for (var day = 20; day < 90; day += 20)
        {
            current = await RefreshAsync(current.RefreshToken, TimeSpan.FromDays(20));
            Assert.True(current.RefreshTokenExpiresAtUtc <= familyEnd);
        }

        // Jour 80 : la dernière rotation est plafonnée à la fin de la famille, pas à +30 jours.
        Assert.Equal(familyEnd, current.RefreshTokenExpiresAtUtc);
        _fixture.Clock.Set(familyEnd);
        var expired = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(current.RefreshToken)));
        Assert.Equal(UserErrors.SessionExpired.Code, expired.Code);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task The_maximum_lifetime_of_a_family_is_a_setting()
    {
        await _fixture.GivenActiveUserAsync();
        _fixture.Settings = _fixture.Settings with { SessionFamilyLifetime = SessionLifetime.Create(TimeSpan.FromDays(2)) };
        var first = await _fixture.GivenSessionAsync();

        var second = await RefreshAsync(first.RefreshToken, TimeSpan.FromDays(1));
        Assert.Equal(_fixture.Clock.UtcNow.AddDays(1), second.RefreshTokenExpiresAtUtc);

        var refused = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(second.RefreshToken, TimeSpan.FromDays(1)));
        Assert.Equal(UserErrors.SessionExpired.Code, refused.Code);
    }
}

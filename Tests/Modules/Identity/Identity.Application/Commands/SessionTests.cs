using Identity.Application.Commands;
using Identity.Application.Queries;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class SessionTests
{
    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task Login_opens_a_session_and_returns_both_tokens()
    {
        var user = await _fixture.GivenActiveUserAsync();

        var tokens = (await _fixture.Login.HandleAsync(new LoginCommand(" CHEF@example.com", IdentityFixture.Password))).Tokens!;

        var session = Assert.Single(user.Sessions);
        Assert.Equal(user.Id.Value, tokens.UserId);
        Assert.Equal(session.Id.Value, tokens.SessionId);
        Assert.Equal(session.ExpiresAtUtc, tokens.RefreshTokenExpiresAtUtc);
        Assert.Equal(_fixture.Clock.UtcNow.Add(SessionLifetime.Default.Duration), session.ExpiresAtUtc);
        Assert.Equal(_fixture.Clock.UtcNow.AddMinutes(15), tokens.AccessTokenExpiresAtUtc);
        // Le jeton brut est remis au client ; seul son hash est conservé.
        Assert.NotEqual(tokens.RefreshToken, session.Hash.Value);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<SessionStarted>(user);
    }

    [Theory]
    [InlineData("chef@example.com", "wrong password")]
    [InlineData("nobody@example.com", IdentityFixture.Password)]
    [InlineData("chef@example.com", null)]
    [InlineData("chef@example.com", "")]
    [InlineData(null, IdentityFixture.Password)]
    public async Task Login_answers_the_same_error_whatever_is_wrong(string? email, string? password)
    {
        var user = await _fixture.GivenActiveUserAsync();

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand(email, password)));

        Assert.Equal("identity.auth.invalid_credentials", exception.Code);
        Assert.Empty(user.Sessions);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Theory]
    [InlineData("chef@example.com")]
    [InlineData("nobody@example.com")]
    public async Task Login_is_blocked_after_too_many_failures_whether_the_account_exists_or_not(string email)
    {
        await _fixture.GivenActiveUserAsync();
        for (var i = 0; i < _fixture.Settings.MaxFailedPasswordAttempts; i++)
        {
            await Assert.ThrowsAsync<UnauthenticatedException>(
                () => _fixture.Login.HandleAsync(new LoginCommand(email, "wrong password")));
        }

        var hashesBefore = _fixture.HashCount;
        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var blocked = await Assert.ThrowsAsync<TooManyRequestsException>(
            () => _fixture.Login.HandleAsync(new LoginCommand(email, IdentityFixture.Password)));

        Assert.Equal("identity.auth.too_many_attempts", blocked.Code);
        Assert.Equal(TimeSpan.FromMinutes(10), blocked.RetryAfter);
        // Une tentative bloquée ne coûte aucun hachage.
        Assert.Equal(hashesBefore, _fixture.HashCount);
    }

    [Fact]
    public async Task The_block_ends_with_its_window_and_a_successful_login_forgets_earlier_failures()
    {
        await _fixture.GivenActiveUserAsync();
        for (var i = 0; i < _fixture.Settings.MaxFailedPasswordAttempts; i++)
        {
            await Assert.ThrowsAsync<UnauthenticatedException>(
                () => _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", "wrong password")));
        }

        _fixture.Clock.Advance(_fixture.Settings.FailedPasswordAttemptWindow);
        Assert.NotNull(await _fixture.GivenSessionAsync());
        Assert.Equal(0, _fixture.FailuresOf("chef@example.com"));

        for (var i = 0; i < _fixture.Settings.MaxFailedPasswordAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<UnauthenticatedException>(
                () => _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", "wrong password")));
        }

        Assert.NotNull(await _fixture.GivenSessionAsync());
    }

    [Fact]
    public async Task Wrong_current_passwords_on_a_signed_in_account_count_towards_the_same_block()
    {
        var user = await _fixture.GivenActiveUserAsync();
        for (var i = 0; i < _fixture.Settings.MaxFailedPasswordAttempts; i++)
        {
            await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.ChangePassword.HandleAsync(
                new ChangePasswordCommand(user.Id.Value, "wrong password", "a brand new password")));
        }

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _fixture.ChangePassword.HandleAsync(
            new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, "a brand new password")));
        await Assert.ThrowsAsync<TooManyRequestsException>(() => _fixture.GivenSessionAsync());
    }

    [Fact]
    public async Task Login_drops_the_sessions_that_have_expired()
    {
        var user = await _fixture.GivenActiveUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await _fixture.GivenSessionAsync();
        }

        _fixture.Clock.Advance(SessionLifetime.Default.Duration);
        var tokens = await _fixture.GivenSessionAsync();

        Assert.Equal(tokens.SessionId, Assert.Single(user.Sessions).Id.Value);
    }

    [Fact]
    public async Task Login_on_an_unknown_account_still_does_the_hashing_work()
    {
        await _fixture.GivenActiveUserAsync();
        var before = _fixture.HashCount;

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand("nobody@example.com", IdentityFixture.Password)));

        Assert.Equal(before + 1, _fixture.HashCount);
    }

    [Fact]
    public async Task Login_does_not_hash_an_oversized_password()
    {
        await _fixture.GivenActiveUserAsync();
        var before = _fixture.HashCount;

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand("nobody@example.com", new string('a', 100_000))));

        Assert.Equal(before, _fixture.HashCount);
    }

    [Fact]
    public async Task Login_on_an_invited_account_without_password_is_invalid_credentials()
    {
        await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand("waiter@example.com", IdentityFixture.Password)));

        Assert.Equal("identity.auth.invalid_credentials", exception.Code);
    }

    [Fact]
    public async Task Login_with_the_right_password_tells_why_the_account_cannot_sign_in()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));

        var pending = await Assert.ThrowsAsync<DomainException>(() => _fixture.GivenSessionAsync());
        Assert.Equal(UserErrors.EmailNotVerified.Code, pending.Code);

        _fixture.Single.Lock(_fixture.Clock);
        var locked = await Assert.ThrowsAsync<DomainException>(() => _fixture.GivenSessionAsync());
        Assert.Equal(UserErrors.Locked.Code, locked.Code);

        // ... mais pas à quelqu'un qui ne connaît pas le mot de passe.
        var wrong = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", "wrong password")));
        Assert.Equal("identity.auth.invalid_credentials", wrong.Code);
    }

    [Fact]
    public async Task Refresh_rotates_the_session_and_the_previous_token_stops_working()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(15));

        var second = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(first.RefreshToken));

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(_fixture.Clock.UtcNow.Add(SessionLifetime.Default.Duration), second.RefreshTokenExpiresAtUtc);
        Assert.Equal(second.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
        DomainAssert.Raised<RefreshTokenRotated>(user);
    }

    [Fact]
    public async Task Refresh_with_an_already_rotated_token_revokes_everything_and_saves_it()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var stolen = await _fixture.GivenSessionAsync();
        var legitimate = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(stolen.RefreshToken));
        var otherDevice = await _fixture.GivenSessionAsync();
        var savesBefore = _fixture.SaveCount;

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(stolen.RefreshToken)));

        Assert.Equal(UserErrors.RefreshTokenReplayed.Code, exception.Code);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
        Assert.Equal(savesBefore + 1, _fixture.SaveCount);
        DomainAssert.Raised<RefreshTokenReplayDetected>(user);

        foreach (var token in new[] { legitimate.RefreshToken, otherDevice.RefreshToken })
        {
            var refused = await Assert.ThrowsAsync<UnauthenticatedException>(
                () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(token)));
            Assert.Equal(UserErrors.SessionRevoked.Code, refused.Code);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("never-issued")]
    public async Task Refresh_refuses_an_unknown_token(string? token)
    {
        await _fixture.GivenActiveUserAsync();
        await _fixture.GivenSessionAsync();

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(token)));

        Assert.Equal("identity.auth.invalid_refresh_token", exception.Code);
    }

    [Fact]
    public async Task Refresh_refuses_an_expired_session_and_a_locked_account_as_unauthenticated()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var tokens = await _fixture.GivenSessionAsync();
        var saves = _fixture.SaveCount;

        _fixture.Clock.Advance(SessionLifetime.Default.Duration);
        var expired = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken)));
        Assert.Equal(UserErrors.SessionExpired.Code, expired.Code);

        user.Lock(_fixture.Clock);
        var locked = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken)));
        Assert.Equal(UserErrors.Locked.Code, locked.Code);
        Assert.Equal(saves, _fixture.SaveCount);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_is_idempotent()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var tokens = await _fixture.GivenSessionAsync();
        var other = await _fixture.GivenSessionAsync();

        await _fixture.Logout.HandleAsync(new LogoutCommand(tokens.RefreshToken));
        await _fixture.Logout.HandleAsync(new LogoutCommand(tokens.RefreshToken));
        await _fixture.Logout.HandleAsync(new LogoutCommand("never-issued"));
        await _fixture.Logout.HandleAsync(new LogoutCommand(null));

        Assert.Equal(other.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
        Assert.Single(user.DomainEvents.OfType<SessionRevoked>());
        var refused = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken)));
        Assert.Equal(UserErrors.SessionRevoked.Code, refused.Code);
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked_one_by_one_or_all_at_once()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();
        _fixture.Clock.Advance(TimeSpan.FromHours(1));
        var second = await _fixture.GivenSessionAsync();
        await _fixture.GivenSessionAsync();

        var listed = await new GetUserSessionsHandler(_fixture, _fixture.Clock)
            .HandleAsync(new GetUserSessionsQuery(user.Id.Value));
        Assert.Equal(3, listed.Count);
        Assert.Equal(first.SessionId, listed[^1].Id);

        await new RevokeSessionHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new RevokeSessionCommand(user.Id.Value, second.SessionId));
        Assert.Equal(2, user.ActiveSessionsAt(_fixture.Clock.UtcNow).Count);

        await new RevokeAllSessionsHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new RevokeAllSessionsCommand(user.Id.Value));
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task RevokeSession_of_an_unknown_session_or_user_is_refused()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var handler = new RevokeSessionHandler(_fixture, _fixture, _fixture.Clock);

        var unknownSession = await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync(new RevokeSessionCommand(user.Id.Value, Guid.CreateVersion7())));
        Assert.Equal(UserErrors.SessionNotFound.Code, unknownSession.Code);

        var unknownUser = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.HandleAsync(new RevokeSessionCommand(Guid.CreateVersion7(), Guid.CreateVersion7())));
        Assert.Equal("identity.user.not_found", unknownUser.Code);
    }
}

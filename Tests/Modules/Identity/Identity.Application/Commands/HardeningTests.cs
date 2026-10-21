using Identity.Application.Commands;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class HardeningTests
{
    private const string NewPassword = "a brand new password";

    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task Login_re_hashes_an_outdated_password_hash_without_signing_anyone_out()
    {
        var user = User.Register(
            Email.Create("chef@example.com"), IdentityFixture.OutdatedHashOf(IdentityFixture.Password), isEmailUnique: true, _fixture.Clock);
        user.VerifyEmail(_fixture.Clock);
        _fixture.Add(user);
        var otherDevice = await _fixture.GivenSessionAsync();
        user.ClearDomainEvents();

        await _fixture.GivenSessionAsync();

        Assert.False(_fixture.NeedsRehash(user.PasswordHash!));
        Assert.True(_fixture.Verify(IdentityFixture.Password, user.PasswordHash!));
        DomainAssert.NotRaised<PasswordChanged>(user);
        Assert.Contains(user.ActiveSessionsAt(_fixture.Clock.UtcNow), session => session.Id.Value == otherDevice.SessionId);
    }

    [Fact]
    public async Task Login_past_the_session_cap_signs_out_the_oldest_devices()
    {
        _fixture.Settings = new IdentitySettings { MaxActiveSessions = 3 };
        var user = await _fixture.GivenActiveUserAsync();
        var tokens = new List<AuthTokensDto>();
        for (var i = 0; i < 5; i++)
        {
            tokens.Add(await _fixture.GivenSessionAsync());
            _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(
            tokens.Skip(2).Select(token => token.SessionId).Order(),
            user.ActiveSessionsAt(_fixture.Clock.UtcNow).Select(session => session.Id.Value).Order());
    }

    [Fact]
    public async Task A_password_known_from_a_data_breach_is_refused_wherever_a_password_is_chosen()
    {
        const string Breached = "password123456";
        _fixture.CompromisedPasswords.Add(Breached);
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));
        var invitation = _fixture.LastTokenSentTo("waiter@example.com");
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var reset = _fixture.LastTokenSentTo("chef@example.com");

        foreach (var attempt in new Func<Task>[]
                 {
                     () => _fixture.Register.HandleAsync(new RegisterUserCommand("new@example.com", Breached)),
                     () => _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(invitation, Breached)),
                     () => _fixture.ChangePassword.HandleAsync(new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, Breached)),
                     () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(reset, Breached)),
                 })
        {
            var exception = await Assert.ThrowsAsync<DomainException>(attempt);
            Assert.Equal("identity.password.compromised", exception.Code);
        }

        // Les refus n'ont rien consommé.
        await _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(reset, NewPassword));
        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(invitation, NewPassword));
    }

    [Fact]
    public async Task Refresh_replayed_after_a_conflict_on_a_token_the_winner_rotated_is_a_conflict_not_a_theft()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var tokens = await _fixture.GivenSessionAsync();
        var winner = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken));

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken, AfterConflict: true)));

        Assert.Equal(winner.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
        DomainAssert.NotRaised<RefreshTokenReplayDetected>(user);
    }

    [Fact]
    public async Task Refresh_replayed_after_a_conflict_on_a_token_still_current_simply_rotates_it()
    {
        await _fixture.GivenActiveUserAsync();
        var tokens = await _fixture.GivenSessionAsync();

        var refreshed = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(tokens.RefreshToken, AfterConflict: true));

        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task Erase_in_self_service_requires_the_password_then_leaves_nothing_personal()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var session = await _fixture.GivenSessionAsync();
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var resetLink = _fixture.LastTokenSentTo("chef@example.com");

        var refused = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value, "wrong password", RequireCurrentPassword: true)));
        Assert.Equal("identity.auth.invalid_credentials", refused.Code);
        Assert.False(user.IsErased);

        await _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value, IdentityFixture.Password, RequireCurrentPassword: true));

        Assert.True(user.IsErased);
        Assert.Equal($"erased-{user.Id.Value:N}@erased.invalid", user.Email.Value);
        Assert.Null(user.PasswordHash);
        Assert.Empty(user.Sessions);
        Assert.Equal((user.Id, "chef@example.com"), Assert.Single(_fixture.Forgotten));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.GivenSessionAsync());
        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(session.RefreshToken)));
        var link = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(resetLink, NewPassword)));
        Assert.Equal("identity.token.invalid", link.Code);
    }

    [Fact]
    public async Task Erase_in_self_service_is_refused_to_the_sole_owner_of_a_restaurant_but_not_to_the_back_office()
    {
        var user = await _fixture.GivenActiveUserAsync();
        _fixture.SoleOwnerships.Add((user.Id, TenantId.From(Guid.CreateVersion7())));

        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value, IdentityFixture.Password, RequireCurrentPassword: true)));
        Assert.Equal("identity.user.sole_owner", refused.Code);
        Assert.False(user.IsErased);

        await _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value));
        Assert.True(user.IsErased);
    }

    [Fact]
    public async Task Erase_by_the_back_office_needs_no_password_and_frees_the_address()
    {
        var user = await _fixture.GivenActiveUserAsync();

        await _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value));
        await _fixture.Erase.HandleAsync(new EraseUserCommand(user.Id.Value));

        Assert.Single(user.DomainEvents.OfType<UserErased>());
        Assert.NotEqual(Guid.Empty, await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", NewPassword)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Erase.HandleAsync(new EraseUserCommand(Guid.CreateVersion7())));
        Assert.Equal("identity.user.not_found", unknown.Code);
    }
}

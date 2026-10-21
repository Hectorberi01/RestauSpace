using Identity.Application.Commands;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class CredentialTests
{
    private const string NewPassword = "a brand new password";

    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task ChangePassword_replaces_the_password_and_keeps_only_the_current_session()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var current = await _fixture.GivenSessionAsync();
        await _fixture.GivenSessionAsync();

        await _fixture.ChangePassword.HandleAsync(
            new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, NewPassword, current.SessionId));

        Assert.True(_fixture.Verify(NewPassword, user.PasswordHash!));
        Assert.Equal(current.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
        DomainAssert.Raised<PasswordChanged>(user);
        await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.GivenSessionAsync());
    }

    [Theory]
    [InlineData("wrong password")]
    [InlineData(null)]
    public async Task ChangePassword_requires_the_current_password(string? currentPassword)
    {
        var user = await _fixture.GivenActiveUserAsync();

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.ChangePassword.HandleAsync(new ChangePasswordCommand(user.Id.Value, currentPassword, NewPassword)));

        Assert.Equal("identity.auth.invalid_credentials", exception.Code);
        Assert.True(_fixture.Verify(IdentityFixture.Password, user.PasswordHash!));
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task ChangePassword_applies_the_password_policy_to_the_new_password()
    {
        var user = await _fixture.GivenActiveUserAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangePassword.HandleAsync(new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, "short")));

        Assert.Equal("identity.password.too_short", exception.Code);
    }

    [Fact]
    public async Task Password_reset_sets_a_new_password_revokes_every_session_and_the_link_works_once()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.GivenSessionAsync();

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal("reset", sent.Kind);
        await _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(sent.Token, NewPassword));

        Assert.True(_fixture.Verify(NewPassword, user.PasswordHash!));
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
        var again = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(sent.Token, "yet another password")));
        Assert.Equal("identity.token.invalid", again.Code);
    }

    [Fact]
    public async Task Password_reset_link_expires_and_a_newer_link_replaces_the_older_one()
    {
        await _fixture.GivenActiveUserAsync();
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var older = _fixture.LastTokenSentTo("chef@example.com");
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var newer = _fixture.LastTokenSentTo("chef@example.com");

        var replaced = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(older, NewPassword)));
        Assert.Equal("identity.token.invalid", replaced.Code);

        _fixture.Clock.Advance(_fixture.Settings.PasswordResetTokenLifetime);
        var expired = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(newer, NewPassword)));
        Assert.Equal("identity.token.invalid", expired.Code);
    }

    [Fact]
    public async Task ChangePassword_with_a_session_id_from_before_a_refresh_keeps_the_session_that_replaced_it()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var first = await _fixture.GivenSessionAsync();
        var second = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(first.RefreshToken));
        var third = await _fixture.Refresh.HandleAsync(new RefreshSessionCommand(second.RefreshToken));

        // "first" a subi deux rotations : sa session n'est même plus conservée par l'agrégat.
        await _fixture.ChangePassword.HandleAsync(
            new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, NewPassword, second.SessionId));

        Assert.Equal(third.SessionId, Assert.Single(user.ActiveSessionsAt(_fixture.Clock.UtcNow)).Id.Value);
    }

    [Fact]
    public async Task ChangePassword_with_an_unknown_session_id_revokes_every_session_instead_of_failing()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.GivenSessionAsync();

        await _fixture.ChangePassword.HandleAsync(
            new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, NewPassword, Guid.CreateVersion7()));

        Assert.True(_fixture.Verify(NewPassword, user.PasswordHash!));
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task A_reset_link_dies_when_the_password_or_the_email_is_changed()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var beforePasswordChange = _fixture.LastTokenSentTo("chef@example.com");
        await _fixture.ChangePassword.HandleAsync(new ChangePasswordCommand(user.Id.Value, IdentityFixture.Password, NewPassword));

        var first = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(beforePasswordChange, "the thief's password")));
        Assert.Equal("identity.token.invalid", first.Code);

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var beforeEmailChange = _fixture.LastTokenSentTo("chef@example.com");
        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(user.Id.Value, "new@example.com"));

        var second = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(beforeEmailChange, "the thief's password")));
        Assert.Equal("identity.token.invalid", second.Code);
        Assert.True(_fixture.Verify(NewPassword, user.PasswordHash!));
    }

    [Fact]
    public async Task No_reset_link_is_sent_to_an_address_the_account_moved_to_without_proving_it()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(user.Id.Value, "not-mine@example.com"));
        _fixture.Sent.Clear();

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("not-mine@example.com"));

        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task The_real_owner_of_a_mailbox_recovers_an_account_someone_else_registered_on_it()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("victim@example.com", "the squatter's password"));

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("victim@example.com"));
        var sent = _fixture.Sent[^1];
        Assert.Equal("reset", sent.Kind);
        await _fixture.ResetPassword.HandleAsync(new ResetPasswordCommand(sent.Token, NewPassword));

        var user = _fixture.Single;
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.IsEmailVerified);
        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.Login.HandleAsync(new LoginCommand("victim@example.com", "the squatter's password")));
        Assert.NotNull(await _fixture.Login.HandleAsync(new LoginCommand("victim@example.com", NewPassword)));
    }

    [Fact]
    public async Task RequestPasswordReset_is_silent_for_an_unknown_or_disabled_account()
    {
        var user = await _fixture.GivenActiveUserAsync();
        user.Disable(_fixture.Clock);

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("nobody@example.com"));

        Assert.Empty(_fixture.Sent);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task RequestPasswordReset_on_an_invited_account_sends_the_invitation_again()
    {
        await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));
        var first = _fixture.LastTokenSentTo("waiter@example.com");

        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("waiter@example.com"));

        var sent = _fixture.Sent[^1];
        Assert.Equal("invitation", sent.Kind);
        Assert.NotEqual(first, sent.Token);
        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(sent.Token, IdentityFixture.Password));
        Assert.Equal(UserStatus.Active, _fixture.Single.Status);
    }

    [Fact]
    public async Task ChangeEmail_sends_a_link_to_the_new_address_and_the_account_stays_active()
    {
        var user = await _fixture.GivenActiveUserAsync();

        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(user.Id.Value, "New@Example.com"));

        Assert.Equal("new@example.com", user.Email.Value);
        Assert.False(user.IsEmailVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal(("verification", "new@example.com"), (sent.Kind, sent.Email));

        await _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(sent.Token, IdentityFixture.Password));
        Assert.True(user.IsEmailVerified);
    }

    [Fact]
    public async Task ChangeEmail_makes_the_link_sent_to_the_previous_address_useless()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("typo@example.com", IdentityFixture.Password));
        var linkToTheWrongAddress = _fixture.LastTokenSentTo("typo@example.com");

        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(_fixture.Single.Id.Value, "chef@example.com"));

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(linkToTheWrongAddress, IdentityFixture.Password)));
        Assert.Equal("identity.token.invalid", exception.Code);
        Assert.False(_fixture.Single.IsEmailVerified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong password")]
    public async Task ChangeEmail_in_self_service_requires_the_current_password(string? currentPassword)
    {
        var user = await _fixture.GivenActiveUserAsync();

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.ChangeEmail.HandleAsync(
            new ChangeEmailCommand(user.Id.Value, "new@example.com", currentPassword, RequireCurrentPassword: true)));

        Assert.Equal("identity.auth.invalid_credentials", exception.Code);
        Assert.Equal("chef@example.com", user.Email.Value);
        Assert.Empty(_fixture.Sent);

        await _fixture.ChangeEmail.HandleAsync(
            new ChangeEmailCommand(user.Id.Value, "new@example.com", IdentityFixture.Password, RequireCurrentPassword: true));
        Assert.Equal("new@example.com", user.Email.Value);
    }

    [Fact]
    public async Task ChangeEmail_to_the_same_address_does_nothing()
    {
        var user = await _fixture.GivenActiveUserAsync();

        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(user.Id.Value, "CHEF@example.com"));

        Assert.True(user.IsEmailVerified);
        Assert.Empty(_fixture.Sent);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task ChangeEmail_refuses_an_address_used_by_another_account()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.GivenActiveUserAsync("other@example.com");

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(user.Id.Value, "other@example.com")));

        Assert.Equal(UserErrors.EmailAlreadyUsed.Code, exception.Code);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task ChangeEmail_of_an_invited_account_sends_the_invitation_to_the_corrected_address()
    {
        await _fixture.Invite.HandleAsync(new InviteUserCommand("typo@example.com"));

        await _fixture.ChangeEmail.HandleAsync(new ChangeEmailCommand(_fixture.Single.Id.Value, "waiter@example.com"));

        var sent = _fixture.Sent[^1];
        Assert.Equal(("invitation", "waiter@example.com"), (sent.Kind, sent.Email));
        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(sent.Token, IdentityFixture.Password));
        Assert.True(_fixture.Single.IsEmailVerified);
    }

    [Fact]
    public async Task Lock_unlock_and_disable_drive_the_account()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.GivenSessionAsync();

        await new LockUserHandler(_fixture, _fixture, _fixture.Clock).HandleAsync(new LockUserCommand(user.Id.Value));
        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Empty(user.ActiveSessionsAt(_fixture.Clock.UtcNow));

        await new UnlockUserHandler(_fixture, _fixture, _fixture.Clock).HandleAsync(new UnlockUserCommand(user.Id.Value));
        Assert.Equal(UserStatus.Active, user.Status);

        await new DisableUserHandler(_fixture, _fixture, _fixture.Clock).HandleAsync(new DisableUserCommand(user.Id.Value));
        Assert.Equal(UserStatus.Disabled, user.Status);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => new LockUserHandler(_fixture, _fixture, _fixture.Clock).HandleAsync(new LockUserCommand(Guid.CreateVersion7())));
        Assert.Equal("identity.user.not_found", unknown.Code);
    }
}

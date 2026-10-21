using Identity.Application.Commands;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class RegistrationTests
{
    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task Register_stores_a_pending_user_with_a_hashed_password_and_emails_a_verification_link()
    {
        var id = await _fixture.Register.HandleAsync(new RegisterUserCommand(" Chef@Example.com ", IdentityFixture.Password));

        var user = _fixture.Single;
        Assert.Equal(user.Id.Value, id);
        Assert.Equal("chef@example.com", user.Email.Value);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.DoesNotContain(IdentityFixture.Password, user.PasswordHash!.Value);
        Assert.True(_fixture.Verify(IdentityFixture.Password, user.PasswordHash));
        Assert.Equal(1, _fixture.SaveCount);
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal(("verification", "chef@example.com"), (sent.Kind, sent.Email));
        DomainAssert.Raised<UserRegistered>(user);
    }

    [Theory]
    [InlineData("not-an-email", IdentityFixture.Password, "common.email.invalid")]
    [InlineData("chef@example.com", null, "identity.password.empty")]
    [InlineData("chef@example.com", "short", "identity.password.too_short")]
    [InlineData("chef@example.com", "            ", "identity.password.empty")]
    public async Task Register_refuses_invalid_input_before_storing_anything(string email, string? password, string code)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Register.HandleAsync(new RegisterUserCommand(email, password)));

        Assert.Equal(code, exception.Code);
        Assert.Equal(0, _fixture.SaveCount);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task Register_refuses_a_password_longer_than_the_maximum()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", new string('a', 129))));

        Assert.Equal("identity.password.too_long", exception.Code);
        Assert.Equal(0, _fixture.HashCount);
    }

    [Fact]
    public async Task Register_refuses_an_email_already_used()
    {
        await _fixture.GivenActiveUserAsync();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Register.HandleAsync(new RegisterUserCommand("CHEF@example.com", IdentityFixture.Password)));

        Assert.Equal(UserErrors.EmailAlreadyUsed.Code, exception.Code);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task VerifyEmail_activates_the_user_and_the_link_works_once()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));
        var token = _fixture.LastTokenSentTo("chef@example.com");

        await _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(token, IdentityFixture.Password));

        Assert.Equal(UserStatus.Active, _fixture.Single.Status);
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(token, IdentityFixture.Password)));
        Assert.Equal("identity.token.invalid", exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("someone else's password")]
    public async Task VerifyEmail_requires_the_password_of_the_account_and_a_wrong_one_leaves_the_link_usable(string? password)
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));
        var token = _fixture.LastTokenSentTo("chef@example.com");
        var saves = _fixture.SaveCount;

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(token, password)));

        Assert.Equal("identity.auth.invalid_credentials", exception.Code);
        Assert.Equal(UserStatus.Pending, _fixture.Single.Status);
        Assert.Equal(saves, _fixture.SaveCount);
        await _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(token, IdentityFixture.Password));
        Assert.Equal(UserStatus.Active, _fixture.Single.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task VerifyEmail_refuses_an_unknown_link(string? token)
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(token, IdentityFixture.Password)));

        Assert.Equal("identity.token.invalid", exception.Code);
        Assert.Equal(UserStatus.Pending, _fixture.Single.Status);
    }

    [Fact]
    public async Task VerifyEmail_refuses_an_expired_link_and_a_new_one_can_be_requested()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));
        var expired = _fixture.LastTokenSentTo("chef@example.com");
        _fixture.Clock.Advance(_fixture.Settings.EmailVerificationTokenLifetime);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(expired, IdentityFixture.Password)));
        Assert.Equal("identity.token.invalid", exception.Code);

        await _fixture.ResendEmailVerification.HandleAsync(new ResendEmailVerificationCommand("chef@example.com"));
        await _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(_fixture.LastTokenSentTo("chef@example.com"), IdentityFixture.Password));
        Assert.Equal(UserStatus.Active, _fixture.Single.Status);
    }

    [Fact]
    public async Task A_link_of_another_purpose_does_not_verify_the_email()
    {
        await _fixture.Register.HandleAsync(new RegisterUserCommand("chef@example.com", IdentityFixture.Password));
        await _fixture.RequestPasswordReset.HandleAsync(new RequestPasswordResetCommand("chef@example.com"));
        var resetToken = _fixture.Sent.Single(sent => sent.Kind == "reset").Token;

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.VerifyEmail.HandleAsync(new VerifyEmailCommand(resetToken, IdentityFixture.Password)));

        Assert.Equal("identity.token.invalid", exception.Code);
    }

    [Theory]
    [InlineData("nobody@example.com")]
    [InlineData("chef@example.com")]
    public async Task ResendEmailVerification_is_silent_for_an_unknown_or_already_verified_account(string email)
    {
        await _fixture.GivenActiveUserAsync();

        await _fixture.ResendEmailVerification.HandleAsync(new ResendEmailVerificationCommand(email));

        Assert.Empty(_fixture.Sent);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task Invite_creates_an_account_without_password_and_accepting_sets_it_and_activates()
    {
        var id = await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));

        var user = _fixture.Single;
        Assert.Equal(user.Id.Value, id);
        Assert.False(user.HasPassword);
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal("invitation", sent.Kind);

        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(sent.Token, IdentityFixture.Password));

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.IsEmailVerified);
        Assert.True(_fixture.Verify(IdentityFixture.Password, user.PasswordHash!));
    }

    [Fact]
    public async Task A_staff_invitation_creates_the_account_first_and_sends_the_link_only_when_asked()
    {
        var id = await _fixture.EnsureInvited.HandleAsync(new EnsureInvitedUserCommand("cook@example.com"));
        var again = await _fixture.EnsureInvited.HandleAsync(new EnsureInvitedUserCommand(" Cook@Example.com "));

        Assert.Equal(id, again);
        Assert.False(_fixture.Single.HasPassword);
        Assert.Empty(_fixture.Sent);

        Assert.True(await _fixture.SendInvitation.HandleAsync(new SendUserInvitationCommand(id)));
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal(("invitation", "cook@example.com"), (sent.Kind, sent.Email));

        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(sent.Token, IdentityFixture.Password));
        Assert.False(await _fixture.SendInvitation.HandleAsync(new SendUserInvitationCommand(id)));
        Assert.False(await _fixture.SendInvitation.HandleAsync(new SendUserInvitationCommand(Guid.NewGuid())));
        Assert.Single(_fixture.Sent);
    }

    [Fact]
    public async Task AcceptInvitation_checks_the_password_before_consuming_the_link()
    {
        await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));
        var token = _fixture.LastTokenSentTo("waiter@example.com");

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(token, "short")));
        Assert.Equal("identity.password.too_short", exception.Code);

        await _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(token, IdentityFixture.Password));
        Assert.True(_fixture.Single.HasPassword);
    }

    [Fact]
    public async Task AcceptInvitation_refuses_an_expired_or_unknown_link()
    {
        await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));
        var token = _fixture.LastTokenSentTo("waiter@example.com");
        _fixture.Clock.Advance(_fixture.Settings.InvitationTokenLifetime);

        foreach (var candidate in new[] { token, "unknown" })
        {
            var exception = await Assert.ThrowsAsync<DomainException>(
                () => _fixture.AcceptInvitation.HandleAsync(new AcceptInvitationCommand(candidate, IdentityFixture.Password)));
            Assert.Equal("identity.token.invalid", exception.Code);
        }

        Assert.False(_fixture.Single.HasPassword);
    }
}

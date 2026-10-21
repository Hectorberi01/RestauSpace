using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class TwoFactorTests
{
    private readonly IdentityFixture _fixture = new();

    private async Task<(User User, byte[] Secret, IReadOnlyList<string> RecoveryCodes)> GivenTwoFactorAsync()
    {
        var user = await _fixture.GivenActiveUserAsync();
        var setup = await _fixture.StartTwoFactor.HandleAsync(new StartTwoFactorSetupCommand(user.Id.Value, IdentityFixture.Password));
        var secret = Convert.FromBase64String(user.PendingTwoFactorSecret!["test.".Length..]);
        var codes = await _fixture.ConfirmTwoFactor.HandleAsync(
            new ConfirmTwoFactorCommand(user.Id.Value, TwoFactorCodes.CodeAt(secret, TwoFactorCodes.StepAt(_fixture.Clock.UtcNow))));
        Assert.Equal(TwoFactorCodes.Base32(secret), setup.Secret);
        Assert.StartsWith("otpauth://totp/RestauSpace:chef%40example.com?secret=", setup.OtpAuthUri, StringComparison.Ordinal);
        return (user, secret, codes.Codes);
    }

    private string CodeNow(byte[] secret) => TwoFactorCodes.CodeAt(secret, TwoFactorCodes.StepAt(_fixture.Clock.UtcNow));

    [Fact]
    public void Codes_follow_the_rfc_6238_test_vector()
    {
        var secret = "12345678901234567890"u8.ToArray();

        Assert.Equal("287082", TwoFactorCodes.CodeAt(secret, 59 / 30));
        Assert.Equal("081804", TwoFactorCodes.CodeAt(secret, 1111111109 / 30));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", TwoFactorCodes.Base32(secret));
    }

    [Fact]
    public async Task With_two_step_verification_the_password_alone_opens_no_session()
    {
        var (user, secret, _) = await GivenTwoFactorAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var first = await _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", IdentityFixture.Password));
        var wrong = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.CompleteTwoFactorLogin.HandleAsync(
            new CompleteTwoFactorLoginCommand(first.TwoFactorChallenge, "000000")));
        var tokens = await _fixture.CompleteTwoFactorLogin.HandleAsync(
            new CompleteTwoFactorLoginCommand(first.TwoFactorChallenge, CodeNow(secret), "Firefox · Linux"));
        var replay = await Assert.ThrowsAsync<DomainException>(() => _fixture.CompleteTwoFactorLogin.HandleAsync(
            new CompleteTwoFactorLoginCommand(first.TwoFactorChallenge, CodeNow(secret))));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var expired = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.CompleteTwoFactorLogin.HandleAsync(
            new CompleteTwoFactorLoginCommand(first.TwoFactorChallenge, CodeNow(secret))));

        Assert.True(first.TwoFactorRequired);
        Assert.Null(first.Tokens);
        Assert.Equal(IdentityApplicationErrors.TwoFactorCodeInvalid, wrong.Error);
        Assert.Equal(user.Id.Value, tokens.UserId);
        Assert.Equal("Firefox · Linux", Assert.Single(user.Sessions).DeviceLabel);
        Assert.Equal(UserErrors.TwoFactorCodeReused, replay.Error);
        Assert.Equal(IdentityApplicationErrors.TwoFactorChallengeInvalid, expired.Error);
    }

    [Fact]
    public async Task A_recovery_code_works_once_and_turning_it_off_needs_the_password_and_a_code()
    {
        var (user, secret, recovery) = await GivenTwoFactorAsync();
        var challenge = (await _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", IdentityFixture.Password))).TwoFactorChallenge;

        var signedIn = await _fixture.CompleteTwoFactorLogin.HandleAsync(new CompleteTwoFactorLoginCommand(challenge, recovery[0].ToUpperInvariant()));
        var left = user.RecoveryCodesLeft;
        var reused = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.CompleteTwoFactorLogin.HandleAsync(new CompleteTwoFactorLoginCommand(challenge, recovery[0])));
        var withoutPassword = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.DisableTwoFactor.HandleAsync(
            new DisableTwoFactorCommand(user.Id.Value, "wrong password", recovery[1])));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await _fixture.DisableTwoFactor.HandleAsync(new DisableTwoFactorCommand(user.Id.Value, IdentityFixture.Password, CodeNow(secret), signedIn.SessionId));
        var plain = await _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", IdentityFixture.Password));

        Assert.Equal(User.RecoveryCodeCount - 1, left);
        Assert.Equal(IdentityApplicationErrors.TwoFactorCodeInvalid, reused.Error);
        Assert.Equal(IdentityApplicationErrors.InvalidCredentials, withoutPassword.Error);
        Assert.False(user.IsTwoFactorEnabled);
        Assert.Equal(0, user.RecoveryCodesLeft);
        Assert.NotNull(plain.Tokens);
        Assert.True(user.Sessions.Single(session => session.Id.Value == signedIn.SessionId).IsUsableAt(_fixture.Clock.UtcNow));
        Assert.DoesNotContain(user.DomainEvents, raised => raised.ToString()!.Contains(TwoFactorCodes.Base32(secret), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wrong_first_code_keeps_it_off_and_a_second_setup_is_refused_once_on()
    {
        var user = await _fixture.GivenActiveUserAsync();
        await _fixture.StartTwoFactor.HandleAsync(new StartTwoFactorSetupCommand(user.Id.Value, IdentityFixture.Password));

        var wrong = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConfirmTwoFactor.HandleAsync(new ConfirmTwoFactorCommand(user.Id.Value, "123")));
        var noPassword = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => _fixture.StartTwoFactor.HandleAsync(new StartTwoFactorSetupCommand(user.Id.Value, "nope")));

        Assert.Equal(IdentityApplicationErrors.TwoFactorCodeInvalid, wrong.Error);
        Assert.Equal(IdentityApplicationErrors.InvalidCredentials, noPassword.Error);
        Assert.False(user.IsTwoFactorEnabled);
    }
}

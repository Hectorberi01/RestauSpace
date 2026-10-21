using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Application.Queries;
using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

/// <summary>Les handlers Application sur les vrais ports : chaque étape est une requête (scope) distincte.</summary>
public sealed class EndToEndTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Sign_up_verify_sign_in_refresh_and_a_replayed_token_signs_out_everywhere()
    {
        const string email = "journey@example.com";

        var userId = await Run(s => new RegisterUserHandler(
            Get<IUserRepository>(s), Get<IUserEmailUniquenessChecker>(s), Get<IPasswordHasher>(s), Get<ICompromisedPasswords>(s), Get<IUserTokens>(s),
            Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s), Get<IdentitySettings>(s), Get<IClock>(s))
            .HandleAsync(new RegisterUserCommand(email, Password)));

        var pending = await Assert.ThrowsAsync<DomainException>(() => LoginAsync(email, Password));
        Assert.Equal(UserErrors.EmailNotVerified.Code, pending.Code);

        await Run(async s =>
        {
            await new VerifyEmailHandler(
                Get<IUserRepository>(s), Get<IUserTokens>(s), Get<PasswordVerifier>(s), Get<IIdentityUnitOfWork>(s), Get<IClock>(s))
                .HandleAsync(new VerifyEmailCommand(database.LastTokenSentTo(email), Password));
            return 0;
        });

        await Assert.ThrowsAsync<UnauthenticatedException>(() => LoginAsync(email, "wrong password"));
        var first = await LoginAsync(email, Password);
        var otherDevice = await LoginAsync(email, Password);
        Assert.Equal(userId, first.UserId);

        var second = await RefreshAsync(first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var sessions = await Run(s => new GetUserSessionsHandler(Get<IUserRepository>(s), Get<IClock>(s))
            .HandleAsync(new GetUserSessionsQuery(userId)));
        Assert.Equal(new[] { second.SessionId, otherDevice.SessionId }.Order(), sessions.Select(session => session.Id).Order());

        database.Clock.Advance(RefreshSessionHandler.ConcurrentRefreshWindow + TimeSpan.FromSeconds(1));
        var replay = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(first.RefreshToken));
        Assert.Equal(UserErrors.RefreshTokenReplayed.Code, replay.Code);

        // La révocation a été validée avant que l'appelant soit refoulé.
        foreach (var token in new[] { second.RefreshToken, otherDevice.RefreshToken })
        {
            var refused = await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(token));
            Assert.Equal(UserErrors.SessionRevoked.Code, refused.Code);
        }
    }

    [Fact]
    public async Task Forgotten_password_reset_then_sign_in_with_the_new_password()
    {
        const string email = "forgetful@example.com";
        await Run(s => new RegisterUserHandler(
            Get<IUserRepository>(s), Get<IUserEmailUniquenessChecker>(s), Get<IPasswordHasher>(s), Get<ICompromisedPasswords>(s), Get<IUserTokens>(s),
            Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s), Get<IdentitySettings>(s), Get<IClock>(s))
            .HandleAsync(new RegisterUserCommand(email, Password)));
        await Run(async s =>
        {
            await new VerifyEmailHandler(
                Get<IUserRepository>(s), Get<IUserTokens>(s), Get<PasswordVerifier>(s), Get<IIdentityUnitOfWork>(s), Get<IClock>(s))
                .HandleAsync(new VerifyEmailCommand(database.LastTokenSentTo(email), Password));
            return 0;
        });
        var before = await LoginAsync(email, Password);

        await Run(async s =>
        {
            await new RequestPasswordResetHandler(
                Get<IUserRepository>(s), Get<IUserTokens>(s), Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s),
                Get<IdentitySettings>(s), Get<IClock>(s))
                .HandleAsync(new RequestPasswordResetCommand(email));
            return 0;
        });
        await Run(async s =>
        {
            await new ResetPasswordHandler(
                Get<IUserRepository>(s), Get<IUserTokens>(s), Get<IPasswordHasher>(s), Get<ICompromisedPasswords>(s), Get<IPasswordAttempts>(s),
                Get<IIdentityUnitOfWork>(s), Get<IClock>(s))
                .HandleAsync(new ResetPasswordCommand(database.LastTokenSentTo(email), "a brand new password"));
            return 0;
        });

        await Assert.ThrowsAsync<UnauthenticatedException>(() => LoginAsync(email, Password));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => RefreshAsync(before.RefreshToken));
        Assert.NotNull(await LoginAsync(email, "a brand new password"));
    }

    private Task<AuthTokensDto> LoginAsync(string email, string password) => Run(async s => (await new LoginHandler(
        Get<IUserRepository>(s), Get<PasswordVerifier>(s), Get<SessionOpener>(s), Get<ITwoFactorChallenges>(s),
        Get<IIdentityUnitOfWork>(s), Get<IdentitySettings>(s), Get<IClock>(s))
        .HandleAsync(new LoginCommand(email, password))).Tokens!);

    private Task<AuthTokensDto> RefreshAsync(string refreshToken) => Run(s => new RefreshSessionHandler(
        Get<IUserRepository>(s), Get<IRefreshTokens>(s), Get<IAccessTokenIssuer>(s), Get<ITenantMemberships>(s), Get<IIdentityUnitOfWork>(s),
        Get<IdentitySettings>(s), Get<IClock>(s))
        .HandleAsync(new RefreshSessionCommand(refreshToken)));

    private async Task<T> Run<T>(Func<IServiceProvider, Task<T>> request)
    {
        await using var scope = database.NewScope();
        return await request(scope.ServiceProvider);
    }

    private static T Get<T>(IServiceProvider services) where T : notnull => services.GetRequiredService<T>();
}

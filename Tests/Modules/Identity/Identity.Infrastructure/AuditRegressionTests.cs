using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

/// <summary>Courses entre deux requêtes, rejouées de façon déterministe : chaque scope est une requête.</summary>
public sealed class AuditRegressionTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "correct horse battery";

    private static int _sequence;

    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task A_verification_link_requested_while_the_email_is_being_changed_cannot_verify_the_new_address()
    {
        var owned = NewEmail();
        var notOwned = NewEmail();
        var userId = await GivenActiveUserWithUnverifiedEmailAsync(owned);

        await using var resend = database.NewScope();
        await using var change = database.NewScope();
        // "Resend" a lu le compte (email = owned)...
        var seenByResend = await Get<IUserRepository>(resend).FindByEmailAsync(Email.Create(owned));
        // ...puis "change email" est commité...
        await ChangeEmailHandler(change).HandleAsync(new ChangeEmailCommand(userId.Value, notOwned));
        // ...et "resend" continue : son lien partirait vers l'adresse que possède l'appelant.
        var staleLink = await Get<IUserTokens>(resend).IssueAsync(
            seenByResend!.Id, UserTokenPurpose.EmailVerification, Clock.UtcNow.AddHours(24));
        await Assert.ThrowsAsync<ConcurrencyException>(() => Get<IIdentityUnitOfWork>(resend).SaveChangesAsync());

        await using var verify = database.NewScope();
        Assert.Null(await Get<IUserTokens>(verify).ConsumeAsync(staleLink, UserTokenPurpose.EmailVerification, Clock.UtcNow));
        Assert.False((await Get<IUserRepository>(verify).FindAsync(userId))!.IsEmailVerified);
    }

    [Fact]
    public async Task Two_links_requested_at_once_one_is_refused_as_a_conflict_not_as_a_database_error()
    {
        var email = NewEmail();
        var userId = await GivenActiveUserAsync(email);

        await using var first = database.NewScope();
        await using var second = database.NewScope();
        var firstUser = await Get<IUserRepository>(first).FindAsync(userId);
        var secondUser = await Get<IUserRepository>(second).FindAsync(userId);
        await Get<IUserTokens>(first).IssueAsync(firstUser!.Id, UserTokenPurpose.PasswordReset, Clock.UtcNow.AddHours(1));
        await Get<IUserTokens>(second).IssueAsync(secondUser!.Id, UserTokenPurpose.PasswordReset, Clock.UtcNow.AddHours(1));

        await Get<IIdentityUnitOfWork>(first).SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => Get<IIdentityUnitOfWork>(second).SaveChangesAsync());
    }

    [Fact]
    public async Task Two_reset_requests_at_once_both_end_silently()
    {
        var email = NewEmail();
        await GivenActiveUserAsync(email);
        var sentBefore = database.Sent.Count;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = database.NewScope();
            await new RequestPasswordResetHandler(
                Get<IUserRepository>(scope), Get<IUserTokens>(scope), Get<IIdentityNotifier>(scope),
                Get<IIdentityUnitOfWork>(scope), Get<IdentitySettings>(scope), Get<IClock>(scope))
                .HandleAsync(new RequestPasswordResetCommand(email));
        }));

        // Quelles que soient les requêtes gagnantes, c'est le dernier lien envoyé qui fonctionne.
        Assert.InRange(database.Sent.Count - sentBefore, 1, 8);
        await using var check = database.NewScope();
        Assert.NotNull(await Get<IUserTokens>(check).ConsumeAsync(
            database.LastTokenSentTo(email), UserTokenPurpose.PasswordReset, Clock.UtcNow));
    }

    [Fact]
    public async Task A_reset_link_stops_working_once_the_email_or_the_password_is_changed()
    {
        var email = NewEmail();
        var userId = await GivenActiveUserAsync(email);

        var beforeEmailChange = await RequestResetAsync(email);
        var newEmail = NewEmail();
        await using (var scope = database.NewScope())
        {
            await ChangeEmailHandler(scope).HandleAsync(new ChangeEmailCommand(userId.Value, newEmail));
        }

        await AssertResetRefusedAsync(beforeEmailChange);

        await VerifyAsync(newEmail);
        var beforePasswordChange = await RequestResetAsync(newEmail);
        await using (var scope = database.NewScope())
        {
            await new ChangePasswordHandler(
                Get<IUserRepository>(scope), Get<IPasswordHasher>(scope), Get<ICompromisedPasswords>(scope), Get<PasswordVerifier>(scope), Get<IUserTokens>(scope),
                Get<IIdentityUnitOfWork>(scope), Get<IClock>(scope))
                .HandleAsync(new ChangePasswordCommand(userId.Value, Password, "a brand new password"));
        }

        await AssertResetRefusedAsync(beforePasswordChange);
    }

    private async Task<string> RequestResetAsync(string email)
    {
        await using var scope = database.NewScope();
        await new RequestPasswordResetHandler(
            Get<IUserRepository>(scope), Get<IUserTokens>(scope), Get<IIdentityNotifier>(scope),
            Get<IIdentityUnitOfWork>(scope), Get<IdentitySettings>(scope), Get<IClock>(scope))
            .HandleAsync(new RequestPasswordResetCommand(email));
        return database.LastTokenSentTo(email);
    }

    private async Task AssertResetRefusedAsync(string token)
    {
        await using var scope = database.NewScope();
        var exception = await Assert.ThrowsAsync<DomainException>(() => new ResetPasswordHandler(
            Get<IUserRepository>(scope), Get<IUserTokens>(scope), Get<IPasswordHasher>(scope), Get<ICompromisedPasswords>(scope), Get<IPasswordAttempts>(scope),
            Get<IIdentityUnitOfWork>(scope), Get<IClock>(scope))
            .HandleAsync(new ResetPasswordCommand(token, "the thief's password")));
        Assert.Equal("identity.token.invalid", exception.Code);
    }

    private async Task<UserId> GivenActiveUserAsync(string email)
    {
        Guid id;
        await using (var scope = database.NewScope())
        {
            id = await new RegisterUserHandler(
                Get<IUserRepository>(scope), Get<IUserEmailUniquenessChecker>(scope), Get<IPasswordHasher>(scope), Get<ICompromisedPasswords>(scope),
                Get<IUserTokens>(scope), Get<IIdentityNotifier>(scope), Get<IIdentityUnitOfWork>(scope),
                Get<IdentitySettings>(scope), Get<IClock>(scope))
                .HandleAsync(new RegisterUserCommand(email, Password));
        }

        await VerifyAsync(email);
        return UserId.From(id);
    }

    private async Task<UserId> GivenActiveUserWithUnverifiedEmailAsync(string email)
    {
        var userId = await GivenActiveUserAsync(NewEmail());
        await using var scope = database.NewScope();
        await ChangeEmailHandler(scope).HandleAsync(new ChangeEmailCommand(userId.Value, email));
        return userId;
    }

    private async Task VerifyAsync(string email)
    {
        await using var scope = database.NewScope();
        await new VerifyEmailHandler(
                Get<IUserRepository>(scope), Get<IUserTokens>(scope), Get<PasswordVerifier>(scope), Get<IIdentityUnitOfWork>(scope), Get<IClock>(scope))
            .HandleAsync(new VerifyEmailCommand(database.LastTokenSentTo(email), Password));
    }

    private static ChangeEmailHandler ChangeEmailHandler(AsyncServiceScope scope) => new(
        Get<IUserRepository>(scope), Get<IUserEmailUniquenessChecker>(scope), Get<PasswordVerifier>(scope),
        Get<IUserTokens>(scope), Get<IIdentityNotifier>(scope), Get<IIdentityUnitOfWork>(scope),
        Get<IdentitySettings>(scope), Get<IClock>(scope));

    private static string NewEmail() => $"user{Interlocked.Increment(ref _sequence)}@audit.example.com";

    private static T Get<T>(AsyncServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();
}

using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public sealed class UserTokensTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static int _sequence;

    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task A_token_is_consumed_once_by_its_purpose_and_only_its_hash_is_stored()
    {
        var userId = await GivenUserAsync();
        var token = await IssueAsync(userId, UserTokenPurpose.PasswordReset, TimeSpan.FromHours(1));

        await using (var scope = database.NewScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().UserTokens
                .SingleAsync(candidate => candidate.UserId == userId);
            Assert.NotEqual(token, stored.TokenHash);
            Assert.Equal(64, stored.TokenHash.Length);
        }

        Assert.Null(await ConsumeAsync(token, UserTokenPurpose.EmailVerification));
        Assert.Null(await ConsumeAsync(token + "x", UserTokenPurpose.PasswordReset));
        Assert.Null(await ConsumeAsync(null, UserTokenPurpose.PasswordReset));
        Assert.Equal(userId, await ConsumeAsync(token, UserTokenPurpose.PasswordReset));
        Assert.Null(await ConsumeAsync(token, UserTokenPurpose.PasswordReset));
    }

    [Fact]
    public async Task A_token_expires_at_its_expiry_instant()
    {
        var userId = await GivenUserAsync();
        var token = await IssueAsync(userId, UserTokenPurpose.EmailVerification, TimeSpan.FromHours(24));

        Clock.Advance(TimeSpan.FromHours(24));

        Assert.Null(await ConsumeAsync(token, UserTokenPurpose.EmailVerification));
    }

    [Fact]
    public async Task A_new_token_replaces_the_previous_one_of_the_same_purpose_only()
    {
        var userId = await GivenUserAsync();
        var older = await IssueAsync(userId, UserTokenPurpose.PasswordReset, TimeSpan.FromHours(1));
        var otherPurpose = await IssueAsync(userId, UserTokenPurpose.EmailVerification, TimeSpan.FromHours(1));
        var newer = await IssueAsync(userId, UserTokenPurpose.PasswordReset, TimeSpan.FromHours(1));

        Assert.Null(await ConsumeAsync(older, UserTokenPurpose.PasswordReset));
        Assert.Equal(userId, await ConsumeAsync(newer, UserTokenPurpose.PasswordReset));
        Assert.Equal(userId, await ConsumeAsync(otherPurpose, UserTokenPurpose.EmailVerification));
    }

    [Fact]
    public async Task Nothing_is_issued_or_consumed_until_the_unit_of_work_saves()
    {
        var userId = await GivenUserAsync();
        string unsaved;
        await using (var scope = database.NewScope())
        {
            unsaved = await scope.ServiceProvider.GetRequiredService<IUserTokens>()
                .IssueAsync(userId, UserTokenPurpose.Invitation, Clock.UtcNow.AddDays(7));
        }

        Assert.Null(await ConsumeAsync(unsaved, UserTokenPurpose.Invitation));

        var token = await IssueAsync(userId, UserTokenPurpose.Invitation, TimeSpan.FromDays(7));
        await using (var scope = database.NewScope())
        {
            Assert.Equal(
                userId,
                await scope.ServiceProvider.GetRequiredService<IUserTokens>()
                    .ConsumeAsync(token, UserTokenPurpose.Invitation, Clock.UtcNow));
        }

        Assert.Equal(userId, await ConsumeAsync(token, UserTokenPurpose.Invitation));
    }

    [Fact]
    public async Task The_same_link_followed_twice_at_once_works_only_once()
    {
        var userId = await GivenUserAsync();
        var token = await IssueAsync(userId, UserTokenPurpose.PasswordReset, TimeSpan.FromHours(1));

        await using var first = database.NewScope();
        await using var second = database.NewScope();
        Assert.NotNull(await first.ServiceProvider.GetRequiredService<IUserTokens>()
            .ConsumeAsync(token, UserTokenPurpose.PasswordReset, Clock.UtcNow));
        Assert.NotNull(await second.ServiceProvider.GetRequiredService<IUserTokens>()
            .ConsumeAsync(token, UserTokenPurpose.PasswordReset, Clock.UtcNow));

        await first.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(
            () => second.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync());
    }

    private async Task<UserId> GivenUserAsync()
    {
        var user = User.Register(
            Email.Create($"user{Interlocked.Increment(ref _sequence)}@tokens.example.com"),
            PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        await using var scope = database.NewScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        return user.Id;
    }

    private async Task<string> IssueAsync(UserId userId, UserTokenPurpose purpose, TimeSpan lifetime)
    {
        await using var scope = database.NewScope();
        var token = await scope.ServiceProvider.GetRequiredService<IUserTokens>()
            .IssueAsync(userId, purpose, Clock.UtcNow.Add(lifetime));
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        return token;
    }

    private async Task<UserId?> ConsumeAsync(string? token, UserTokenPurpose purpose)
    {
        await using var scope = database.NewScope();
        var userId = await scope.ServiceProvider.GetRequiredService<IUserTokens>().ConsumeAsync(token, purpose, Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        return userId;
    }
}

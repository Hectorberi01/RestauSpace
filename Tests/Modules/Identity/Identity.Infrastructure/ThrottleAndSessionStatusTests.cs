using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public sealed class ThrottleAndSessionStatusTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static int _sequence;

    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task Failures_block_an_address_at_the_limit_until_the_window_that_started_with_the_first_one_ends()
    {
        var account = NewEmail();
        var firstFailureAt = Clock.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await BlockedUntilAsync(account, maxFailures: 3));
            await Attempts(s => s.RecordFailureAsync(account, Clock.UtcNow, Window));
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(firstFailureAt + Window, await BlockedUntilAsync(account, maxFailures: 3));
        Assert.Null(await BlockedUntilAsync(account, maxFailures: 4));
        Assert.Null(await BlockedUntilAsync(NewEmail(), maxFailures: 3));

        Clock.Advance(Window);
        Assert.Null(await BlockedUntilAsync(account, maxFailures: 3));
        // Une nouvelle fenêtre repart de zéro.
        await Attempts(s => s.RecordFailureAsync(account, Clock.UtcNow, Window));
        Assert.Null(await BlockedUntilAsync(account, maxFailures: 2));
    }

    [Fact]
    public async Task A_reset_forgets_the_failures_and_simultaneous_failures_are_all_counted()
    {
        var account = NewEmail();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Attempts(s => s.RecordFailureAsync(account, Clock.UtcNow, Window))));

        Assert.NotNull(await BlockedUntilAsync(account, maxFailures: 20));
        Assert.Null(await BlockedUntilAsync(account, maxFailures: 21));

        await Attempts(s => s.ResetAsync(account));
        Assert.Null(await BlockedUntilAsync(account, maxFailures: 1));
    }

    [Fact]
    public async Task A_session_is_active_while_usable_or_just_rotated_into_a_usable_one_on_an_active_account()
    {
        var user = User.Register(NewEmail(), PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        user.VerifyEmail(Clock);
        var first = user.StartSession(Token(), SessionLifetime.Default, Clock);
        var signedOut = user.StartSession(Token(), SessionLifetime.Default, Clock);
        user.RevokeSession(signedOut.Id, Clock);
        var shortLived = user.StartSession(Token(), SessionLifetime.Create(TimeSpan.FromMinutes(5)), Clock);
        await SaveAsync(user, add: true);

        Assert.True(await IsActiveAsync(user.Id, first.Id));
        Assert.False(await IsActiveAsync(user.Id, signedOut.Id));
        Assert.False(await IsActiveAsync(user.Id, SessionId.New()));
        Assert.False(await IsActiveAsync(UserId.New(), first.Id));
        Assert.False(await IsActiveAsync(user.Id, shortLived.Id, Clock.UtcNow.AddMinutes(5)));

        var second = (await RotateAsync(user.Id, first)).NewSession!;
        Assert.True(await IsActiveAsync(user.Id, first.Id));
        Assert.True(await IsActiveAsync(user.Id, second.Id));

        var third = (await RotateAsync(user.Id, second)).NewSession!;
        Assert.False(await IsActiveAsync(user.Id, first.Id));
        Assert.True(await IsActiveAsync(user.Id, second.Id));

        await using (var scope = database.NewScope())
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);
            loaded!.Lock(Clock);
            await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        }

        Assert.False(await IsActiveAsync(user.Id, third.Id));
        Assert.False(await IsActiveAsync(user.Id, second.Id));
    }

    private async Task<SessionRotationResult> RotateAsync(UserId userId, RefreshSession current)
    {
        await using var scope = database.NewScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(userId);
        var result = loaded!.RotateSession(current.Hash, Token(), SessionLifetime.Default, Clock);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        return result;
    }

    private async Task SaveAsync(User user, bool add)
    {
        await using var scope = database.NewScope();
        if (add)
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        }

        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
    }

    private async Task<bool> IsActiveAsync(UserId userId, SessionId sessionId, DateTimeOffset? at = null)
    {
        await using var scope = database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionStatus>().IsActiveAsync(userId, sessionId, at ?? Clock.UtcNow);
    }

    private Task<DateTimeOffset?> BlockedUntilAsync(Email account, int maxFailures) =>
        Attempts(s => s.BlockedUntilAsync(account, Clock.UtcNow, maxFailures, Window));

    private async Task<T> Attempts<T>(Func<IPasswordAttempts, Task<T>> call)
    {
        await using var scope = database.NewScope();
        return await call(scope.ServiceProvider.GetRequiredService<IPasswordAttempts>());
    }

    private async Task Attempts(Func<IPasswordAttempts, Task> call)
    {
        await using var scope = database.NewScope();
        await call(scope.ServiceProvider.GetRequiredService<IPasswordAttempts>());
    }

    private static Email NewEmail() => Email.Create($"user{Interlocked.Increment(ref _sequence)}@throttle.example.com");

    private static TokenHash Token() => TokenHash.Create($"throttle-token-{Interlocked.Increment(ref _sequence)}-{Guid.NewGuid():N}");
}

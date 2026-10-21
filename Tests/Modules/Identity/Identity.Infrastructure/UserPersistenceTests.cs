using System.Text.Json;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public sealed class UserPersistenceTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static int _sequence;

    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task A_saved_user_is_read_back_with_its_state_and_all_its_sessions()
    {
        var user = NewActiveUser();
        var kept = user.StartSession(Token(), SessionLifetime.Default, Clock);
        var rotatedFrom = Token();
        user.StartSession(rotatedFrom, SessionLifetime.Create(TimeSpan.FromDays(7)), Clock);
        var rotation = user.RotateSession(rotatedFrom, Token(), SessionLifetime.Default, Clock);
        user.RevokeSession(kept.Id, Clock);
        await SaveNewAsync(user);

        var loaded = await LoadAsync(user.Id);

        Assert.NotNull(loaded);
        Assert.Equal(user.Email, loaded.Email);
        Assert.Equal(user.PasswordHash, loaded.PasswordHash);
        Assert.Equal(UserStatus.Active, loaded.Status);
        Assert.Null(loaded.StatusBeforeLock);
        Assert.Equal(user.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(user.EmailVerifiedAtUtc, loaded.EmailVerifiedAtUtc);
        Assert.Empty(loaded.DomainEvents);

        Assert.Equal(3, loaded.Sessions.Count);
        var revoked = loaded.Sessions.Single(session => session.Id == kept.Id);
        Assert.Equal(SessionRevocationReason.UserRequested, revoked.RevocationReason);
        Assert.Equal(kept.Hash, revoked.Hash);
        Assert.Equal(kept.ExpiresAtUtc, revoked.ExpiresAtUtc);
        var rotated = loaded.Sessions.Single(session => session.WasRotated);
        Assert.Equal(rotation.NewSession!.Id, rotated.ReplacedBy);
        Assert.Null(rotated.RevocationReason);
        Assert.Equal(rotation.NewSession.Id, Assert.Single(loaded.ActiveSessionsAt(Clock.UtcNow)).Id);
    }

    [Fact]
    public async Task An_invited_user_has_no_password_and_a_locked_one_remembers_its_previous_status()
    {
        var invited = User.RegisterInvited(NewEmail(), isEmailUnique: true, Clock);
        var locked = NewActiveUser();
        locked.Lock(Clock);
        await SaveNewAsync(invited);
        await SaveNewAsync(locked);

        Assert.Null((await LoadAsync(invited.Id))!.PasswordHash);
        var reloaded = await LoadAsync(locked.Id);
        Assert.Equal(UserStatus.Locked, reloaded!.Status);
        Assert.Equal(UserStatus.Active, reloaded.StatusBeforeLock);
    }

    [Fact]
    public async Task A_user_is_found_by_email_and_by_any_of_its_session_tokens_even_a_rotated_one()
    {
        var user = NewActiveUser();
        var first = Token();
        var second = Token();
        user.StartSession(first, SessionLifetime.Default, Clock);
        user.RotateSession(first, second, SessionLifetime.Default, Clock);
        await SaveNewAsync(user);

        await using var scope = database.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.Equal(user.Id, (await users.FindByEmailAsync(user.Email))!.Id);
        Assert.Equal(user.Id, (await users.FindBySessionTokenAsync(first))!.Id);
        var bySecond = await users.FindBySessionTokenAsync(second);
        Assert.Equal(2, bySecond!.Sessions.Count);
        Assert.Null(await users.FindBySessionTokenAsync(Token()));
        Assert.Null(await users.FindByEmailAsync(NewEmail()));
        Assert.Null(await users.FindAsync(UserId.New()));
    }

    [Fact]
    public async Task Sessions_dropped_by_the_aggregate_are_deleted_from_the_database()
    {
        var user = NewActiveUser();
        var current = Token();
        user.StartSession(current, SessionLifetime.Default, Clock);
        await SaveNewAsync(user);

        for (var i = 0; i < 6; i++)
        {
            await using var scope = database.NewScope();
            var loaded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindBySessionTokenAsync(current);
            var next = Token();
            Assert.False(loaded!.RotateSession(current, next, SessionLifetime.Default, Clock).ReplayDetected);
            await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
            current = next;
        }

        Assert.Equal(2, await CountSessionsAsync(user.Id));

        await using (var scope = database.NewScope())
        {
            Clock.Advance(SessionLifetime.Default.Duration);
            var loaded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);
            Assert.Equal(2, loaded!.PurgeExpiredSessions(Clock));
            await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        }

        Assert.Equal(0, await CountSessionsAsync(user.Id));
    }

    [Fact]
    public async Task Two_concurrent_rotations_of_the_same_token_the_second_one_is_refused_then_seen_as_a_replay()
    {
        var user = NewActiveUser();
        var token = Token();
        user.StartSession(token, SessionLifetime.Default, Clock);
        await SaveNewAsync(user);

        await using (var first = database.NewScope())
        await using (var second = database.NewScope())
        {
            var firstCopy = await first.ServiceProvider.GetRequiredService<IUserRepository>().FindBySessionTokenAsync(token);
            var secondCopy = await second.ServiceProvider.GetRequiredService<IUserRepository>().FindBySessionTokenAsync(token);

            firstCopy!.RotateSession(token, Token(), SessionLifetime.Default, Clock);
            await first.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
            secondCopy!.RotateSession(token, Token(), SessionLifetime.Default, Clock);
            var exception = await Assert.ThrowsAsync<ConcurrencyException>(
                () => second.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync());

            Assert.Equal("identity.user.concurrent_update", exception.Code);
        }

        Assert.Equal(2, await CountSessionsAsync(user.Id));

        // Le perdant réessaie sur un état frais : son token est désormais un token déjà renouvelé.
        await using var retry = database.NewScope();
        var reloaded = await retry.ServiceProvider.GetRequiredService<IUserRepository>().FindBySessionTokenAsync(token);
        Assert.True(reloaded!.RotateSession(token, Token(), SessionLifetime.Default, Clock).ReplayDetected);
        await retry.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        Assert.Empty((await LoadAsync(user.Id))!.ActiveSessionsAt(Clock.UtcNow));
    }

    [Fact]
    public async Task A_revocation_concurrent_with_a_rotation_is_refused_too()
    {
        var user = NewActiveUser();
        var token = Token();
        var session = user.StartSession(token, SessionLifetime.Default, Clock);
        await SaveNewAsync(user);

        await using var rotating = database.NewScope();
        await using var revoking = database.NewScope();
        var rotatingCopy = await rotating.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);
        var revokingCopy = await revoking.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);

        rotatingCopy!.RotateSession(token, Token(), SessionLifetime.Default, Clock);
        await rotating.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        revokingCopy!.RevokeSession(session.Id, Clock);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => revoking.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task The_email_uniqueness_checker_ignores_the_user_changing_its_own_email()
    {
        var user = NewActiveUser();
        await SaveNewAsync(user);

        await using var scope = database.NewScope();
        var checker = scope.ServiceProvider.GetRequiredService<IUserEmailUniquenessChecker>();

        Assert.False(await checker.IsUniqueAsync(user.Email, null));
        Assert.False(await checker.IsUniqueAsync(user.Email, UserId.New()));
        Assert.True(await checker.IsUniqueAsync(user.Email, user.Id));
        Assert.True(await checker.IsUniqueAsync(NewEmail(), null));
    }

    [Fact]
    public async Task Two_sign_ups_racing_on_the_same_email_the_second_one_is_told_the_email_is_used()
    {
        var email = NewEmail();
        await SaveNewAsync(User.Register(email, PasswordHash.Create("hash"), isEmailUnique: true, Clock));

        // Le checker a répondu "unique" aux deux avant que l'un ou l'autre soit sauvegardé.
        var late = User.Register(email, PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        var exception = await Assert.ThrowsAsync<DomainException>(() => SaveNewAsync(late));

        Assert.Equal(UserErrors.EmailAlreadyUsed.Code, exception.Code);
        Assert.NotEmpty(late.DomainEvents);
    }

    [Fact]
    public async Task Saving_stores_the_domain_events_in_the_outbox_with_plain_values()
    {
        var user = User.Register(NewEmail(), PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        user.VerifyEmail(Clock);
        var session = user.StartSession(Token(), SessionLifetime.Default, Clock);

        await SaveNewAsync(user);

        Assert.Empty(user.DomainEvents);
        await using var scope = database.NewScope();
        var messages = (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OutboxMessages
                .AsNoTracking().OrderBy(message => message.Id).ToListAsync())
            .Where(message => message.Content.Contains(user.Id.Value.ToString()))
            .ToList();
        Assert.Equal(["UserRegistered", "EmailVerified", "SessionStarted"], messages.Select(message => message.Type));

        using var registered = JsonDocument.Parse(messages[0].Content);
        Assert.Equal(user.Id.Value, registered.RootElement.GetProperty("UserId").GetGuid());
        Assert.Equal(user.Email.Value, registered.RootElement.GetProperty("Email").GetString());
        using var started = JsonDocument.Parse(messages[2].Content);
        Assert.Equal(session.Id.Value, started.RootElement.GetProperty("SessionId").GetGuid());
        // Aucun secret n'atteint jamais l'outbox.
        Assert.All(messages, message => Assert.DoesNotContain("hash", message.Content, StringComparison.OrdinalIgnoreCase));
    }

    private static Email NewEmail() => Email.Create($"user{Interlocked.Increment(ref _sequence)}@persistence.example.com");

    private static TokenHash Token() => TokenHash.Create($"token-hash-{Interlocked.Increment(ref _sequence)}-{Guid.NewGuid():N}");

    [Fact]
    public async Task The_directory_searches_emails_literally_and_filters_by_status()
    {
        var marker = "dir" + Guid.NewGuid().ToString("N")[..8];
        var active = User.Register(Email.Create($"{marker}.alpha@example.com"), PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        active.VerifyEmail(Clock);
        var locked = User.Register(Email.Create($"{marker}.beta@example.com"), PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        locked.VerifyEmail(Clock);
        locked.Lock(Clock);
        await SaveNewAsync(active);
        await SaveNewAsync(locked);

        await using var scope = database.NewScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        Assert.Equal(2, (await directory.ListAsync(marker, null, 0, 10)).Count);
        Assert.Equal(locked.Id, Assert.Single(await directory.ListAsync(marker, UserStatus.Locked, 0, 10)).Id);
        Assert.Empty(await directory.ListAsync(marker + "%", null, 0, 10));
        Assert.Single(await directory.ListAsync(marker, null, 1, 10));
    }

    private User NewActiveUser()
    {
        var user = User.Register(NewEmail(), PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        user.VerifyEmail(Clock);
        return user;
    }

    private async Task SaveNewAsync(User user)
    {
        await using var scope = database.NewScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
    }

    private async Task<User?> LoadAsync(UserId id)
    {
        await using var scope = database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(id);
    }

    private async Task<int> CountSessionsAsync(UserId id)
    {
        await using var scope = database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from identity.sessions where user_id = {id.Value}")
            .SingleAsync();
    }
}

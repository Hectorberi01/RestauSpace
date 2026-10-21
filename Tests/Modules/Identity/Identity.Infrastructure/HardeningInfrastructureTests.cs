using System.Net;
using System.Text;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Application;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

public sealed class HardeningInfrastructureTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static int _sequence;

    private FakeClock Clock => database.Clock;

    [Fact]
    public void A_hash_made_with_fewer_iterations_still_verifies_and_is_reported_as_outdated()
    {
        var hasher = database.Get<IPasswordHasher>();
        var legacy = PasswordHash.Create(
            new PasswordHasher<object>(Options.Create(new PasswordHasherOptions { IterationCount = 10_000 }))
                .HashPassword(new object(), "correct horse battery"));

        Assert.True(hasher.Verify("correct horse battery", legacy));
        Assert.True(hasher.NeedsRehash(legacy));
        Assert.False(hasher.NeedsRehash(hasher.Hash("correct horse battery")));
        Assert.True(hasher.NeedsRehash(PasswordHash.Create("not-one-of-our-hashes")));
    }

    [Fact]
    public void The_same_password_typed_as_composed_or_decomposed_characters_matches()
    {
        var hasher = database.Get<IPasswordHasher>();
        var composed = "caf\u00E9 cr\u00E8me 2026";
        var decomposed = composed.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(composed, decomposed);

        Assert.True(hasher.Verify(decomposed, hasher.Hash(composed)));
        Assert.True(hasher.Verify(composed, hasher.Hash(decomposed)));
    }

    private sealed class RangeApiStub : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            // SHA-1("password") = 5BAA6 1E4C9B93F3F0682250B6CF8331B7EE68FD8
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:9545824\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:0\r\n"),
            });
        }
    }

    [Fact]
    public async Task The_compromised_password_check_sends_only_a_hash_prefix_and_accepts_the_password_if_the_service_fails()
    {
        var stub = new RangeApiStub();
        await using var services = database.NewProvider(s => s.AddIdentityCompromisedPasswordCheck(stub));
        var check = services.GetRequiredService<ICompromisedPasswords>();

        Assert.True(await check.IsCompromisedAsync("password"));
        Assert.False(await check.IsCompromisedAsync("a password nobody ever leaked 2026"));
        Assert.Equal("/range/5BAA6", stub.Paths[0]);
        Assert.All(stub.Paths, path => Assert.Equal("/range/".Length + 5, path.Length));

        stub.Status = HttpStatusCode.ServiceUnavailable;
        Assert.False(await check.IsCompromisedAsync("password"));
    }

    [Fact]
    public async Task By_default_no_password_is_sent_anywhere()
    {
        Assert.False(await database.Get<ICompromisedPasswords>().IsCompromisedAsync("password"));
    }

    [Fact]
    public async Task The_periodic_purge_removes_expired_sessions_and_links_but_keeps_what_replay_detection_needs()
    {
        var maintenance = database.Get<IdentityMaintenance>();
        var user = NewActiveUser();
        var forgotten = user.StartSession(Token(), SessionLifetime.Create(TimeSpan.FromDays(1)), Clock);
        var rotatedFrom = Token();
        var rotated = user.StartSession(rotatedFrom, SessionLifetime.Create(TimeSpan.FromDays(1)), Clock);
        var live = user.RotateSession(rotatedFrom, Token(), SessionLifetime.Default, Clock).NewSession!;
        await SaveAsync(user);
        await using (var scope = database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserTokens>()
                .IssueAsync(user.Id, UserTokenPurpose.PasswordReset, Clock.UtcNow.AddHours(1));
            await scope.ServiceProvider.GetRequiredService<IUserTokens>()
                .IssueAsync(user.Id, UserTokenPurpose.Invitation, Clock.UtcNow.AddDays(7));
            await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
        }

        Clock.Advance(TimeSpan.FromDays(2));
        try
        {
            Assert.True(await maintenance.PurgeAsync() >= 2);

            await using var scope = database.NewScope();
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);
            Assert.Equal(
                new[] { rotated.Id.Value, live.Id.Value }.Order(), loaded!.Sessions.Select(session => session.Id.Value).Order());
            Assert.DoesNotContain(loaded.Sessions, session => session.Id == forgotten.Id);
            Assert.Equal(
                [UserTokenPurpose.Invitation],
                await context.UserTokens.Where(token => token.UserId == user.Id).Select(token => token.Purpose).ToListAsync());
        }
        finally
        {
            Clock.Advance(TimeSpan.FromDays(-2));
        }
    }

    [Fact]
    public async Task Erasing_an_account_leaves_no_address_in_the_database_and_frees_it_for_a_new_account()
    {
        var email = Email.Create($"erase{Interlocked.Increment(ref _sequence)}@hardening.example.com");
        var user = User.Register(email, PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        user.VerifyEmail(Clock);
        user.StartSession(Token(), SessionLifetime.Default, Clock);
        await SaveAsync(user);
        await using (var scope = database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPasswordAttempts>().RecordFailureAsync(email, Clock.UtcNow, TimeSpan.FromMinutes(15));
        }

        await using (var scope = database.NewScope())
        {
            var s = scope.ServiceProvider;
            await new EraseUserHandler(
                s.GetRequiredService<IUserRepository>(), s.GetRequiredService<PasswordVerifier>(), s.GetRequiredService<IUserTokens>(),
                s.GetRequiredService<IUserTraces>(), s.GetRequiredService<ITenantMemberships>(), s.GetRequiredService<IIdentityUnitOfWork>(),
                s.GetRequiredService<IClock>())
                .HandleAsync(new EraseUserCommand(user.Id.Value));
        }

        await using (var scope = database.NewScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var erased = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(user.Id);
            Assert.True(erased!.IsErased);
            Assert.Equal(UserStatus.Disabled, erased.Status);
            Assert.Empty(erased.Sessions);
            Assert.Null(erased.PasswordHash);

            var outbox = await context.OutboxMessages.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(outbox, message => message.Content.Contains(email.Value));
            Assert.Equal(
                ["UserErased"],
                outbox.Where(message => message.Content.Contains(user.Id.Value.ToString())).Select(message => message.Type));
            Assert.False(await context.PasswordAttempts.AnyAsync(attempt => attempt.Email == email.Value));
        }

        // L'adresse est de nouveau libre.
        await SaveAsync(User.Register(email, PasswordHash.Create("hash"), isEmailUnique: true, Clock));
    }

    private User NewActiveUser()
    {
        var user = User.Register(
            Email.Create($"user{Interlocked.Increment(ref _sequence)}@hardening.example.com"),
            PasswordHash.Create("hash"), isEmailUnique: true, Clock);
        user.VerifyEmail(Clock);
        return user;
    }

    private async Task SaveAsync(User user)
    {
        await using var scope = database.NewScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
    }

    private static TokenHash Token() => TokenHash.Create($"hardening-{Interlocked.Increment(ref _sequence)}-{Guid.NewGuid():N}");
}

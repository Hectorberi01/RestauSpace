using System.Text.RegularExpressions;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Identity.Infrastructure;

/// <summary>
/// Le module avec son vrai pipeline d'emails et ses services d'arrière-plan en marche, comme dans un hôte : les emails
/// sont lus depuis un faux serveur SMTP local.
/// </summary>
public sealed class EmailAndOutboxTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private const string Password = "correct horse battery";

    private static int _sequence;

    private readonly FakeSmtpServer _smtp = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.NewProvider(services => services.AddIdentitySmtpEmails(
            emails =>
            {
                emails.ProductName = "Chez Test";
                emails.VerifyEmailUrl = "https://app.example.com/verify?token={token}";
                emails.ResetPasswordUrl = "https://app.example.com/reset?token={token}";
                emails.AcceptInvitationUrl = "https://app.example.com/invite?token={token}";
            },
            smtp =>
            {
                smtp.Host = "127.0.0.1";
                smtp.Port = _smtp.Port;
                smtp.Security = SmtpSecurity.None;
                smtp.FromAddress = "no-reply@example.com";
                smtp.FromName = "Chez Test";
            }));
        foreach (var service in _services.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var service in _services.GetServices<IHostedService>())
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _services.DisposeAsync();
        await _smtp.DisposeAsync();
    }

    [Fact]
    public async Task The_emailed_links_really_arrive_by_smtp_and_work()
    {
        var email = NewEmail();
        await Run(s => Register(s).HandleAsync(new RegisterUserCommand(email, Password)));

        var verification = await _smtp.WaitForAsync(mail => mail.To == email && mail.Subject.Contains("confirmez"));
        Assert.Equal("\"Chez Test\" <no-reply@example.com>", verification.From);
        Assert.Contains("activer votre compte Chez Test", verification.Body);
        var verifyToken = TokenIn(verification.Body, "https://app.example.com/verify?token=");
        await Run(async s =>
        {
            await new VerifyEmailHandler(Get<IUserRepository>(s), Get<IUserTokens>(s), Get<PasswordVerifier>(s), Get<IIdentityUnitOfWork>(s), Get<IClock>(s))
                .HandleAsync(new VerifyEmailCommand(verifyToken, Password));
            return 0;
        });

        await Run(async s =>
        {
            await new RequestPasswordResetHandler(
                Get<IUserRepository>(s), Get<IUserTokens>(s), Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s),
                Get<IdentitySettings>(s), Get<IClock>(s)).HandleAsync(new RequestPasswordResetCommand(email));
            return 0;
        });
        var reset = await _smtp.WaitForAsync(mail => mail.To == email && mail.Subject.Contains("réinitialisation"));
        var resetToken = TokenIn(reset.Body, "https://app.example.com/reset?token=");

        await Run(async s =>
        {
            await ResetPassword(s).HandleAsync(new ResetPasswordCommand(resetToken, "a brand new password"));
            return 0;
        });

        // Le changement de mot de passe par ce biais est annoncé lui aussi, depuis l'outbox.
        var notice = await _smtp.WaitForAsync(mail => mail.To == email && mail.Subject.Contains("mot de passe a été modifié"));
        Assert.DoesNotContain("token", notice.Body);
    }

    [Fact]
    public async Task A_change_of_email_is_announced_to_the_previous_address_with_the_new_one_masked()
    {
        var previous = NewEmail();
        var userId = await Run(s => Register(s).HandleAsync(new RegisterUserCommand(previous, Password)));
        var next = NewEmail();

        await Run(async s =>
        {
            await new ChangeEmailHandler(
                Get<IUserRepository>(s), Get<IUserEmailUniquenessChecker>(s), Get<PasswordVerifier>(s), Get<IUserTokens>(s),
                Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s), Get<IdentitySettings>(s), Get<IClock>(s))
                .HandleAsync(new ChangeEmailCommand(userId, next));
            return 0;
        });

        var notice = await _smtp.WaitForAsync(mail => mail.To == previous && mail.Subject.Contains("adresse email de votre compte a changé"));
        Assert.Contains($"{next[0]}***@", notice.Body);
        Assert.DoesNotContain(next, notice.Body);
        await _smtp.WaitForAsync(mail => mail.To == next && mail.Subject.Contains("confirmez"));

        // Le dispatcher a marqué ce qu'il a publié.
        await using var scope = _services.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OutboxMessages
            .Where(message => message.Type == "UserEmailChanged" && message.ProcessedAtUtc == null)
            .ToListAsync();
        Assert.DoesNotContain(pending, message => message.Content.Contains(userId.ToString()));
    }

    private static RegisterUserHandler Register(IServiceProvider s) => new(
        Get<IUserRepository>(s), Get<IUserEmailUniquenessChecker>(s), Get<IPasswordHasher>(s), Get<ICompromisedPasswords>(s),
        Get<IUserTokens>(s), Get<IIdentityNotifier>(s), Get<IIdentityUnitOfWork>(s), Get<IdentitySettings>(s), Get<IClock>(s));

    private static ResetPasswordHandler ResetPassword(IServiceProvider s) => new(
        Get<IUserRepository>(s), Get<IUserTokens>(s), Get<IPasswordHasher>(s), Get<ICompromisedPasswords>(s),
        Get<IPasswordAttempts>(s), Get<IIdentityUnitOfWork>(s), Get<IClock>(s));

    private static string TokenIn(string body, string linkPrefix)
    {
        var match = Regex.Match(body, Regex.Escape(linkPrefix) + @"(\S+)");
        Assert.True(match.Success, body);
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private async Task<T> Run<T>(Func<IServiceProvider, Task<T>> request)
    {
        await using var scope = _services.CreateAsyncScope();
        return await request(scope.ServiceProvider);
    }

    private static T Get<T>(IServiceProvider services) where T : notnull => services.GetRequiredService<T>();

    private static string NewEmail() => $"user{Interlocked.Increment(ref _sequence)}@mail.example.com";
}

public sealed class OutboxDispatcherTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private sealed class FirstConsumer : IOutboxConsumer
    {
        public List<OutboxEnvelope> Received { get; } = [];

        public string? FailOnType { get; set; }

        public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            if (message.Type == FailOnType)
            {
                throw new InvalidOperationException("consumer down");
            }

            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondConsumer : IOutboxConsumer
    {
        public List<OutboxEnvelope> Received { get; } = [];

        public string? RejectType { get; set; }

        public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            if (message.Type == RejectType)
            {
                throw new DomainException(new DomainError("test.refused", "Refused."));
            }

            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    private async Task<(ServiceProvider Services, FirstConsumer First, SecondConsumer Second, OutboxDispatcher<IdentityDbContext> Dispatcher)> NewDispatcherAsync()
    {
        var first = new FirstConsumer();
        var second = new SecondConsumer();
        var services = database.NewProvider(s => s.AddSingleton<IOutboxConsumer>(first).AddSingleton<IOutboxConsumer>(second));
        var dispatcher = services.GetRequiredService<OutboxDispatcher<IdentityDbContext>>();
        while (await dispatcher.DispatchPendingAsync() > 0)
        {
        }

        first.Received.Clear();
        second.Received.Clear();
        return (services, first, second, dispatcher);
    }

    private async Task<User> SaveVerifiedAndLockedUserAsync(ServiceProvider services, string email)
    {
        var user = User.Register(Email.Create(email), PasswordHash.Create("hash"), isEmailUnique: true, database.Clock);
        user.VerifyEmail(database.Clock);
        user.Lock(database.Clock);
        await SaveAsync(services, user);
        return user;
    }

    [Fact]
    public async Task A_failing_consumer_is_retried_later_alone_while_the_next_messages_go_on()
    {
        var (services, first, second, dispatcher) = await NewDispatcherAsync();
        await using var owned = services;
        var user = await SaveVerifiedAndLockedUserAsync(services, "dispatch@example.com");

        first.FailOnType = "EmailVerified";
        Assert.Equal(3, await dispatcher.DispatchPendingAsync());
        Assert.Equal(["UserRegistered", "UserLocked"], first.Received.Select(message => message.Type));
        Assert.Equal(["UserRegistered", "EmailVerified", "UserLocked"], second.Received.Select(message => message.Type));
        Assert.Equal(0, await dispatcher.DispatchPendingAsync());

        first.FailOnType = null;
        database.Clock.Advance(OutboxDispatcher<IdentityDbContext>.RetryDelay(1));
        Assert.Equal(1, await dispatcher.DispatchPendingAsync());

        Assert.Equal(["UserRegistered", "UserLocked", "EmailVerified"], first.Received.Select(message => message.Type));
        Assert.Equal(3, second.Received.Count);
        Assert.All(first.Received, message => Assert.Equal("identity", message.Module));
        Assert.Contains(user.Id.Value.ToString(), first.Received[0].Content);
        Assert.All(first.Received, message => Assert.Equal(1, message.Version));
    }

    [Fact]
    public async Task A_business_refusal_goes_to_the_dead_letters_at_once_and_a_replay_delivers_it_to_that_consumer_only()
    {
        var (services, first, second, dispatcher) = await NewDispatcherAsync();
        await using var owned = services;
        await SaveVerifiedAndLockedUserAsync(services, "refused@example.com");
        var monitor = services.GetServices<IOutboxMonitor>().Single(candidate => candidate.Module == "identity");

        second.RejectType = "UserLocked";
        Assert.Equal(3, await dispatcher.DispatchPendingAsync());

        var dead = Assert.Single(await monitor.ListDeadLettersAsync(1, 50), letter => letter.Type == "UserLocked");
        var refused = Assert.Single(dead.Deliveries);
        Assert.Equal(("Rejected", "test.refused"), (refused.Status, refused.LastError));
        Assert.Contains(refused.Consumer, typeof(SecondConsumer).FullName!, StringComparison.Ordinal);
        Assert.True((await monitor.GetStatusAsync()).DeadLettered >= 1);

        second.RejectType = null;
        Assert.True(await monitor.ReplayAsync(dead.MessageId));
        Assert.False(await monitor.ReplayAsync(dead.MessageId));
        Assert.Equal(1, await dispatcher.DispatchPendingAsync());

        Assert.Single(first.Received, message => message.Type == "UserLocked");
        Assert.Single(second.Received, message => message.Type == "UserLocked");
        Assert.DoesNotContain(await monitor.ListDeadLettersAsync(1, 50), letter => letter.MessageId == dead.MessageId);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_dead_lettered_after_the_last_attempt()
    {
        var (services, first, _, dispatcher) = await NewDispatcherAsync();
        await using var owned = services;
        await SaveVerifiedAndLockedUserAsync(services, "poison@example.com");
        var monitor = services.GetServices<IOutboxMonitor>().Single(candidate => candidate.Module == "identity");
        first.FailOnType = "UserRegistered";

        for (var attempt = 1; attempt <= OutboxDispatcher<IdentityDbContext>.MaxAttempts; attempt++)
        {
            await dispatcher.DispatchPendingAsync();
            database.Clock.Advance(OutboxDispatcher<IdentityDbContext>.RetryDelay(attempt));
        }

        var dead = Assert.Single(await monitor.ListDeadLettersAsync(1, 50), letter => letter.Type == "UserRegistered" && letter.Deliveries.Any(delivery => delivery.Status == "DeadLettered"));
        Assert.Equal(OutboxDispatcher<IdentityDbContext>.MaxAttempts, dead.AttemptCount);
        Assert.Equal("InvalidOperationException", Assert.Single(dead.Deliveries).LastError);
        Assert.Equal(0, await dispatcher.DispatchPendingAsync());
        Assert.True(await monitor.ReplayAsync(dead.MessageId));
    }

    [Fact]
    public async Task Only_one_dispatcher_runs_a_module_at_a_time()
    {
        var (services, first, _, dispatcher) = await NewDispatcherAsync();
        await using var owned = services;
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.OpenConnectionAsync();
        var key = "outbox:identity";
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({key}, 0)) AS \"Value\"").SingleAsync());
        await SaveVerifiedAndLockedUserAsync(services, "locked-out@example.com");

        Assert.Equal(0, await dispatcher.DispatchPendingAsync());
        Assert.Empty(first.Received);

        await context.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({key}, 0)) AS \"Value\"").SingleAsync();
        Assert.Equal(3, await dispatcher.DispatchPendingAsync());
    }

    [Fact]
    public async Task Published_messages_are_deleted_after_the_retention_period_and_pending_ones_never()
    {
        await using var services = database.NewProvider(_ => { });
        var dispatcher = services.GetRequiredService<OutboxDispatcher<IdentityDbContext>>();
        var old = User.Register(Email.Create("retention-old@example.com"), PasswordHash.Create("hash"), isEmailUnique: true, database.Clock);
        await SaveAsync(services, old);
        await dispatcher.DispatchPendingAsync();

        database.Clock.Advance(TimeSpan.FromDays(31));
        var recent = User.Register(Email.Create("retention-pending@example.com"), PasswordHash.Create("hash"), isEmailUnique: true, database.Clock);
        await SaveAsync(services, recent);

        Assert.True(await dispatcher.DeleteOldMessagesAsync() >= 1);

        await using var scope = services.CreateAsyncScope();
        var contents = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OutboxMessages
            .Select(message => message.Content).ToListAsync();
        Assert.DoesNotContain(contents, content => content.Contains(old.Id.Value.ToString()));
        Assert.Contains(contents, content => content.Contains(recent.Id.Value.ToString()));
        database.Clock.Advance(TimeSpan.FromDays(-31));
    }

    private static async Task SaveAsync(ServiceProvider services, User user)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>().SaveChangesAsync();
    }
}

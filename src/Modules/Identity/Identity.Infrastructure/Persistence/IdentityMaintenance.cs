using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Ménage périodique, pour les comptes qui ne se reconnectent jamais : sessions expirées, liens envoyés par email
/// expirés et anciens compteurs d'échecs. Peut tourner sans risque sur plusieurs instances.
/// </summary>
public sealed class IdentityMaintenance(IServiceScopeFactory scopes, IClock clock, ILogger<IdentityMaintenance> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = clock.UtcNow;
        var sessions = context.Set<RefreshSession>();

        // Même règle que User.PurgeExpiredSessions : une session expirée et remplacée reste tant que son successeur est
        // utilisable, pour que le rejeu de son jeton soit encore détecté.
        var purged = await sessions
            .Where(session => session.ExpiresAtUtc <= now
                && !sessions.Any(next =>
                    (SessionId?)next.Id == session.ReplacedBy && next.RevokedAtUtc == null && next.ExpiresAtUtc > now))
            .ExecuteDeleteAsync(cancellationToken);
        purged += await context.UserTokens.Where(token => token.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);
        var attemptsLimit = now.AddDays(-1);
        purged += await context.PasswordAttempts
            .Where(attempt => attempt.WindowStartedAtUtc <= attemptsLimit)
            .ExecuteDeleteAsync(cancellationToken);
        return purged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var purged = await PurgeAsync(stoppingToken);
                logger.LogInformation("Identity maintenance removed {Count} expired rows.", purged);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Identity maintenance failed; it will run again later.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

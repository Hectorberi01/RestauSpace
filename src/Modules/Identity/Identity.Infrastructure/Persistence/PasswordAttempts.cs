using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Écrit immédiatement, hors de l'unit of work : une tentative échouée doit être comptée même si sa requête
/// n'enregistre rien, et des échecs concurrents doivent tous compter (upsert atomique).
/// </summary>
internal sealed class PasswordAttempts(IdentityDbContext context) : IPasswordAttempts
{
    public async Task<DateTimeOffset?> BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var windowFloor = nowUtc - window;
        var startedAtUtc = await context.PasswordAttempts
            .Where(attempt => attempt.Email == account.Value
                && attempt.WindowStartedAtUtc > windowFloor
                && attempt.Failures >= maxFailures)
            .Select(attempt => (DateTimeOffset?)attempt.WindowStartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return startedAtUtc + window;
    }

    public async Task RecordFailureAsync(
        Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var windowFloor = nowUtc - window;
        await context.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM identity.password_attempts WHERE window_started_at_utc <= {windowFloor};
            INSERT INTO identity.password_attempts (email, failures, window_started_at_utc)
            VALUES ({account.Value}, 1, {nowUtc})
            ON CONFLICT (email) DO UPDATE SET failures = identity.password_attempts.failures + 1;
            """,
            cancellationToken);
    }

    public Task ResetAsync(Email account, CancellationToken cancellationToken = default) =>
        context.PasswordAttempts.Where(attempt => attempt.Email == account.Value).ExecuteDeleteAsync(cancellationToken);
}

using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Échecs du code 2FA, dans la même table que ceux du mot de passe mais sous une autre clé (« 2fa: » + empreinte de
/// l'adresse) : les deux compteurs sont indépendants.
/// </summary>
internal sealed class TwoFactorAttempts(IdentityDbContext context) : ITwoFactorAttempts
{
    public async Task<DateTimeOffset?> BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var key = Key(account);
        var windowFloor = nowUtc - window;
        var startedAtUtc = await context.PasswordAttempts
            .Where(attempt => attempt.Email == key && attempt.WindowStartedAtUtc > windowFloor && attempt.Failures >= maxFailures)
            .Select(attempt => (DateTimeOffset?)attempt.WindowStartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return startedAtUtc + window;
    }

    public async Task RecordFailureAsync(Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var key = Key(account);
        var windowFloor = nowUtc - window;
        await context.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM identity.password_attempts WHERE window_started_at_utc <= {windowFloor};
            INSERT INTO identity.password_attempts (email, failures, window_started_at_utc)
            VALUES ({key}, 1, {nowUtc})
            ON CONFLICT (email) DO UPDATE SET failures = identity.password_attempts.failures + 1;
            """,
            cancellationToken);
    }

    public Task ResetAsync(Email account, CancellationToken cancellationToken = default)
    {
        var key = Key(account);
        return context.PasswordAttempts.Where(attempt => attempt.Email == key).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Clé courte et sans l'adresse en clair.</summary>
    private static string Key(Email account) =>
        "2fa:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account.Value)));
}

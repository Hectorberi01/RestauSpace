namespace Identity.Application.Abstractions;

/// <summary>
/// Échecs du code de double authentification, comptés À PART de ceux du mot de passe : une connexion réussie par mot de
/// passe ne remet jamais ce compteur à zéro. Seul un code juste le fait.
/// </summary>
public interface ITwoFactorAttempts
{
    Task<DateTimeOffset?> BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken = default);

    Task RecordFailureAsync(Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken = default);

    Task ResetAsync(Email account, CancellationToken cancellationToken = default);
}

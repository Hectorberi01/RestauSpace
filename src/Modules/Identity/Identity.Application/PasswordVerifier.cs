using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application;

/// <summary>
/// Le seul endroit où un mot de passe en clair est vérifié. Les échecs sont comptés par ADRESSE de compte, existante
/// ou non : au-delà de la limite, toute tentative est refusée jusqu'à la fin de la fenêtre, bon mot de passe compris.
/// Le blocage est temporaire à dessein ; un vrai verrouillage permettrait à n'importe qui de bloquer définitivement
/// n'importe quel compte.
/// </summary>
public sealed class PasswordVerifier(
    IPasswordHasher passwords, IPasswordAttempts attempts, IdentitySettings settings, IClock clock)
{
    /// <returns>Le même mot de passe aux paramètres de hachage actuels si le hash stocké est obsolète, sinon null.</returns>
    public async Task<PasswordHash?> EnsureAsync(
        Email account, PasswordHash? hash, string? password, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var blockedUntil = await attempts.BlockedUntilAsync(
            account, now, settings.MaxFailedPasswordAttempts, settings.FailedPasswordAttemptWindow, cancellationToken);
        if (blockedUntil is { } until)
        {
            // Avant tout hachage : un appelant bloqué ne doit pas non plus coûter ce travail lent.
            throw new TooManyRequestsException(IdentityApplicationErrors.TooManyAttempts, until - now);
        }

        if (!Matches(hash, password))
        {
            await attempts.RecordFailureAsync(account, now, settings.FailedPasswordAttemptWindow, cancellationToken);
            throw new UnauthenticatedException(IdentityApplicationErrors.InvalidCredentials);
        }

        await attempts.ResetAsync(account, cancellationToken);
        return passwords.NeedsRehash(hash!) ? passwords.Hash(password!) : null;
    }

    private bool Matches(PasswordHash? hash, string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > PasswordPolicy.MaxLength)
        {
            return false;
        }

        if (hash is null)
        {
            // Même travail qu'une vraie vérification : le temps de réponse ne doit pas révéler si le compte existe.
            passwords.Hash(password);
            return false;
        }

        return passwords.Verify(password, hash);
    }
}

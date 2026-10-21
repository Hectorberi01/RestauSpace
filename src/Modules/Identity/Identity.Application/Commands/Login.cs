using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Connexion par mot de passe : ouvre une session et renvoie ses tokens. <paramref name="Device"/> : appareil et
/// navigateur déduits par l'API (« Chrome · macOS »), affichés dans la liste des sessions. Avec la validation en deux
/// étapes, aucune session n'est ouverte : un jeton de courte durée attend le code (<see cref="CompleteTwoFactorLoginCommand"/>).
/// </summary>
public sealed record LoginCommand(string? Email, string? Password, string? Device = null) : ICommand<LoginResultDto>
{
    public override string ToString() => "LoginCommand (***)";
}

/// <summary>Soit les tokens de la session, soit le jeton qui attend le code de l'application.</summary>
public sealed record LoginResultDto(AuthTokensDto? Tokens, string? TwoFactorChallenge)
{
    public bool TwoFactorRequired => TwoFactorChallenge is not null;
}

public sealed class LoginHandler(
    IUserRepository users,
    PasswordVerifier passwords,
    SessionOpener sessions,
    ITwoFactorChallenges challenges,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<LoginCommand, LoginResultDto>
{
    public async Task<LoginResultDto> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            throw new UnauthenticatedException(IdentityApplicationErrors.InvalidCredentials);
        }

        var email = Email.Create(command.Email);
        var user = await users.FindByEmailAsync(email, cancellationToken);
        var upgradedHash = await passwords.EnsureAsync(email, user?.PasswordHash, command.Password, cancellationToken);

        // Seul quelqu'un qui connaît le mot de passe apprend que le compte est en attente, verrouillé ou désactivé.
        if (user!.IsTwoFactorEnabled)
        {
            user.EnsureCanStartSession();
            if (upgradedHash is not null)
            {
                user.UpgradePasswordHash(upgradedHash);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new LoginResultDto(null, challenges.Issue(user.Id, clock.UtcNow + settings.TwoFactorChallengeLifetime));
        }

        var tokens = sessions.Open(user, command.Device, upgradedHash);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LoginResultDto(tokens, null);
    }
}

/// <summary>Ouvre une session pour un utilisateur dont l'identité est prouvée (mot de passe, et code s'il le faut).</summary>
public sealed class SessionOpener(IRefreshTokens refreshTokens, IAccessTokenIssuer accessTokens, IdentitySettings settings, IClock clock)
{
    public AuthTokensDto Open(User user, string? device, PasswordHash? upgradedHash)
    {
        ArgumentNullException.ThrowIfNull(user);
        var refreshToken = refreshTokens.Generate();
        // Ménage : sans lui, chaque connexion jamais faite resterait dans l'agrégat et serait chargée à chaque fois.
        user.PurgeExpiredSessions(clock);
        var session = user.StartSession(refreshToken.Hash, settings.SessionLifetime, clock, device);

        if (upgradedHash is not null && user.Status != UserStatus.Disabled)
        {
            user.UpgradePasswordHash(upgradedHash);
        }

        // Un plafond d'appareils connectés simultanément : les sessions les plus anciennes font de la place.
        var live = user.ActiveSessionsAt(clock.UtcNow).OrderBy(candidate => candidate.CreatedAtUtc).ToList();
        foreach (var oldest in live.Take(Math.Max(0, live.Count - settings.MaxActiveSessions)))
        {
            user.RevokeSession(oldest.Id, clock);
        }

        return AuthTokensDto.From(user, session, accessTokens.Issue(user, session.Id), refreshToken);
    }
}

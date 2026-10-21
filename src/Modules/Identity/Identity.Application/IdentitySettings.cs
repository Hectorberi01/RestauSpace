using Identity.Domain.Users;

namespace Identity.Application;

public sealed record IdentitySettings
{
    public SessionLifetime SessionLifetime { get; init; } = SessionLifetime.Default;

    /// <summary>Temps laissé pour saisir le code de l'application après le mot de passe.</summary>
    public TimeSpan TwoFactorChallengeLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Nom affiché dans l'application d'authentification.</summary>
    public string TwoFactorIssuer { get; init; } = "RestauSpace";
    public TimeSpan EmailVerificationTokenLifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan InvitationTokenLifetime { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan PasswordResetTokenLifetime { get; init; } = TimeSpan.FromHours(1);
    public int MaxFailedPasswordAttempts { get; init; } = 10;
    public TimeSpan FailedPasswordAttemptWindow { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxActiveSessions { get; init; } = 20;
}

using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Persistence;

/// <summary>Jeton à usage unique envoyé par email. Seul le hash est stocké ; un au plus par utilisateur et usage.</summary>
public sealed class UserToken
{
    // Réservé à l'ORM.
    private UserToken()
    {
        TokenHash = null!;
    }

    internal UserToken(UserId userId, UserTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAtUtc)
    {
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public long Id { get; private set; }

    public UserId UserId { get; private set; }

    public UserTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }
}

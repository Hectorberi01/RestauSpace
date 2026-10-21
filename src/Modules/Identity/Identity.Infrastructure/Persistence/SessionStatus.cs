using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Infrastructure.Persistence;

internal sealed class SessionStatus(IdentityDbContext context) : ISessionStatus
{
    /// <summary>
    /// Vrai tant que le compte est actif et que la session est utilisable, ou vient d'être remplacée par une session
    /// utilisable : un access token émis juste avant un refresh reste valide, pas celui d'une session déconnectée.
    /// </summary>
    public Task<bool> IsActiveAsync(
        UserId userId, SessionId sessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var sessions = context.Set<RefreshSession>();
        return context.Users
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .SelectMany(user => user.Sessions)
            .Where(session => session.Id == sessionId)
            .AnyAsync(
                session => (session.RevokedAtUtc == null && session.ExpiresAtUtc > nowUtc)
                    || sessions.Any(next =>
                        (SessionId?)next.Id == session.ReplacedBy
                        && next.RevokedAtUtc == null
                        && next.ExpiresAtUtc > nowUtc),
                cancellationToken);
    }
}

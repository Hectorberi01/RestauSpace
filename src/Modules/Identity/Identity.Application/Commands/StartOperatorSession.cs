using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Session de caisse d'un membre reconnu par son PIN (vérifié par Staff, appelant de confiance via l'hôte, jamais exposé
/// en route) : un access token marqué caisse, valable <see cref="IdentitySettings.TillSessionLifetime"/>, SANS refresh
/// token. Les permissions sont restreintes par l'hôte. Null si le compte ne peut pas ouvrir de session.
/// </summary>
public sealed record StartOperatorSessionCommand(Guid UserId, Guid TenantId, Guid TillId, Guid LocationId, string? Device)
    : ICommand<OperatorSessionDto?>;

public sealed record OperatorSessionDto(Guid UserId, Guid SessionId, string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, Guid TenantId);

public sealed class StartOperatorSessionHandler(
    IUserRepository users,
    ITenantMemberships memberships,
    IRefreshTokens refreshTokens,
    IAccessTokenIssuer accessTokens,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<StartOperatorSessionCommand, OperatorSessionDto?>
{
    public async Task<OperatorSessionDto?> HandleAsync(StartOperatorSessionCommand command, CancellationToken cancellationToken = default)
    {
        if (command.UserId == Guid.Empty || command.TenantId == Guid.Empty || command.TillId == Guid.Empty || command.LocationId == Guid.Empty)
        {
            return null;
        }

        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        var tenantId = TenantId.From(command.TenantId);
        if (user is null || !user.CanStartSession || !await memberships.CanActForTenantAsync(user.Id, tenantId, cancellationToken))
        {
            return null;
        }

        // Jeton jamais remis : la session ne peut pas être renouvelée.
        var unusable = refreshTokens.Generate();
        user.PurgeExpiredSessions(clock);
        var session = user.StartSession(unusable.Hash, SessionLifetime.Create(settings.TillSessionLifetime), clock, command.Device);
        var live = user.ActiveSessionsAt(clock.UtcNow).OrderBy(candidate => candidate.CreatedAtUtc).ToList();
        foreach (var oldest in live.Take(Math.Max(0, live.Count - settings.MaxActiveSessions)))
        {
            user.RevokeSession(oldest.Id, clock);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var till = new TillAccess(command.TillId, command.LocationId, session.ExpiresAtUtc);
        var token = accessTokens.Issue(user, session.Id, tenantId, till);
        return new OperatorSessionDto(user.Id.Value, session.Id.Value, token.Value, token.ExpiresAtUtc, tenantId.Value);
    }
}

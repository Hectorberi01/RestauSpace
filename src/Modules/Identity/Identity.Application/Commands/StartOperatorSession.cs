using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Session d'un membre du personnel reconnu par son PIN sur une caisse partagée (vérifié par Staff, appelant de
/// confiance via l'hôte, jamais exposé en route) : une session à lui, dont l'access token agit pour ce restaurant après
/// la même vérification d'appartenance qu'une sélection de restaurant. Null si son compte ne peut pas ouvrir de session.
/// </summary>
public sealed record StartOperatorSessionCommand(Guid UserId, Guid TenantId, string? Device) : ICommand<OperatorSessionDto?>;

public sealed record OperatorSessionDto(AuthTokensDto Tokens, Guid TenantId);

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
        if (command.UserId == Guid.Empty || command.TenantId == Guid.Empty)
        {
            return null;
        }

        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        var tenantId = TenantId.From(command.TenantId);
        if (user is null || !user.CanStartSession || !await memberships.CanActForTenantAsync(user.Id, tenantId, cancellationToken))
        {
            return null;
        }

        var refreshToken = refreshTokens.Generate();
        user.PurgeExpiredSessions(clock);
        var session = user.StartSession(refreshToken.Hash, settings.SessionLifetime, clock, command.Device);
        var live = user.ActiveSessionsAt(clock.UtcNow).OrderBy(candidate => candidate.CreatedAtUtc).ToList();
        foreach (var oldest in live.Take(Math.Max(0, live.Count - settings.MaxActiveSessions)))
        {
            user.RevokeSession(oldest.Id, clock);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new OperatorSessionDto(AuthTokensDto.From(user, session, accessTokens.Issue(user, session.Id, tenantId), refreshToken), tenantId.Value);
    }
}

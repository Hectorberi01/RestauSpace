using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Termine la session de ce refresh token. Idempotent : une session inconnue ou déjà terminée est un succès.</summary>
public sealed record LogoutCommand(string? RefreshToken) : ICommand;

public sealed class LogoutHandler(
    IUserRepository users, IRefreshTokens refreshTokens, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<LogoutCommand>
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return;
        }

        var hash = refreshTokens.Hash(command.RefreshToken);
        var user = await users.FindBySessionTokenAsync(hash, cancellationToken);
        var session = user?.Sessions.SingleOrDefault(candidate => candidate.Hash == hash);
        if (user == null || session == null || session.IsRevoked)
        {
            return;
        }

        user.RevokeSession(session.Id, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

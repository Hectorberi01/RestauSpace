using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Termine une session de l'utilisateur (« déconnecter cet appareil »).</summary>
public sealed record RevokeSessionCommand(Guid UserId, Guid SessionId) : ICommand;

public sealed class RevokeSessionHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task HandleAsync(RevokeSessionCommand command, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionId.From(command.SessionId);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        user.RevokeSession(sessionId, clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

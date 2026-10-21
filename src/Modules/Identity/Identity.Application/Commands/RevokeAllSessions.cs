using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Met fin à toutes les sessions de l'utilisateur (« se déconnecter partout »).</summary>
public sealed record RevokeAllSessionsCommand(Guid UserId) : ICommand;

public sealed class RevokeAllSessionsHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RevokeAllSessionsCommand>
{
    public async Task HandleAsync(RevokeAllSessionsCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        user.RevokeAllSessions(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Déverrouille le compte : il revient au statut qu'il avait avant le verrouillage.</summary>
public sealed record UnlockUserCommand(Guid UserId) : ICommand;

public sealed class UnlockUserHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UnlockUserCommand>
{
    public async Task HandleAsync(UnlockUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        user.Unlock(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

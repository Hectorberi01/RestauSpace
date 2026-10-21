using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Désactive définitivement le compte (état terminal) et révoque ses sessions.</summary>
public sealed record DisableUserCommand(Guid UserId) : ICommand;

public sealed class DisableUserHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<DisableUserCommand>
{
    public async Task HandleAsync(DisableUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        user.Disable(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

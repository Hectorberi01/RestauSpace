using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Verrouille le compte et révoque ses sessions (back office, ou une policy anti-force brute).</summary>
public sealed record LockUserCommand(Guid UserId) : ICommand;

public sealed class LockUserHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<LockUserCommand>
{
    public async Task HandleAsync(LockUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        user.Lock(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

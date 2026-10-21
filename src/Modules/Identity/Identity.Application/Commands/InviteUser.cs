using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Crée un compte pour un tiers, sans mot de passe ; le lien envoyé par email lui permet d'en choisir un.</summary>
public sealed record InviteUserCommand(string? Email) : ICommand<Guid>;

public sealed class InviteUserHandler(
    IUserRepository users,
    IUserEmailUniquenessChecker emailUniqueness,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<InviteUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(InviteUserCommand command, CancellationToken cancellationToken = default)
    {
        var email = Email.Create(command.Email);
        var isEmailUnique = await emailUniqueness.IsUniqueAsync(email, null, cancellationToken);

        var user = User.RegisterInvited(email, isEmailUnique, clock);
        users.Add(user);
        var token = await tokens.IssueAsync(user.Id, UserTokenPurpose.Invitation, clock.UtcNow + settings.InvitationTokenLifetime, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.SendInvitationAsync(email, token, cancellationToken);

        return user.Id.Value;
    }
}

public sealed record EnsureInvitedUserCommand(string? Email) : ICommand<Guid>;

public sealed class EnsureInvitedUserHandler(
    IUserRepository users,
    IUserEmailUniquenessChecker emailUniqueness,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<EnsureInvitedUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(EnsureInvitedUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = Email.Create(command.Email);
        if (await users.FindByEmailAsync(email, cancellationToken) is { } existing)
        {
            return existing.Id.Value;
        }

        var isEmailUnique = await emailUniqueness.IsUniqueAsync(email, null, cancellationToken);
        var user = User.RegisterInvited(email, isEmailUnique, clock);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return user.Id.Value;
    }
}

public sealed record SendUserInvitationCommand(Guid UserId) : ICommand<bool>;

public sealed class SendUserInvitationHandler(
    IUserRepository users,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<SendUserInvitationCommand, bool>
{
    public async Task<bool> HandleAsync(SendUserInvitationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = command.UserId == Guid.Empty ? null : await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user is null || user.IsErased || user.HasPassword || user.Status != UserStatus.Pending)
        {
            return false;
        }

        var token = await tokens.IssueAsync(user.Id, UserTokenPurpose.Invitation, clock.UtcNow + settings.InvitationTokenLifetime, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.SendInvitationAsync(user.Email, token, cancellationToken);
        return true;
    }
}

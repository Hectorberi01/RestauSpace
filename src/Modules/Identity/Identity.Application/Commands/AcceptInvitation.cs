using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Suivre le lien d'invitation prouve l'adresse : le mot de passe est défini et le compte activé.</summary>
public sealed record AcceptInvitationCommand(string? Token, string? Password) : ICommand;

public sealed class AcceptInvitationHandler(
    IUserRepository users,
    IUserTokens tokens,
    IPasswordHasher passwords,
    ICompromisedPasswords compromised,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<AcceptInvitationCommand>
{
    public async Task HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var password = await PasswordPolicy.CheckAsync(command.Password, compromised, cancellationToken);
        var userId = await tokens.ConsumeAsync(command.Token, UserTokenPurpose.Invitation, clock.UtcNow, cancellationToken);
        var user = userId is { } id ? await users.FindAsync(id, cancellationToken) : null;
        if (user == null)
        {
            throw new DomainException(IdentityApplicationErrors.TokenInvalid);
        }

        user.AcceptInvitation(passwords.Hash(password), clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

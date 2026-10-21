using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>Définit un nouveau mot de passe via le lien reçu par e-mail. Toutes les sessions sont révoquées.</summary>
public sealed record ResetPasswordCommand(string? Token, string? NewPassword) : ICommand;

public sealed class ResetPasswordHandler(
    IUserRepository users,
    IUserTokens tokens,
    IPasswordHasher passwords,
    ICompromisedPasswords compromised,
    IPasswordAttempts attempts,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        var newPassword = await PasswordPolicy.CheckAsync(command.NewPassword, compromised, cancellationToken);
        var userId = await tokens.ConsumeAsync(
            command.Token, UserTokenPurpose.PasswordReset, clock.UtcNow, cancellationToken);
        var user = userId is { } id ? await users.FindAsync(id, cancellationToken) : null;
        if (user == null)
        {
            throw new DomainException(IdentityApplicationErrors.TokenInvalid);
        }

        user.ChangePassword(passwords.Hash(newPassword), clock);
        if (!user.IsEmailVerified)
        {
            // Suivre le lien reçu par e-mail prouve l'adresse, comme le fait l'acceptation d'une invitation.
            user.VerifyEmail(clock);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Le titulaire de la boîte a choisi un nouveau mot de passe : les échecs antérieurs ne doivent pas le bloquer.
        await attempts.ResetAsync(user.Email, cancellationToken);
    }
}

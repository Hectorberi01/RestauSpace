using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Remplace l'email du compte. La nouvelle adresse reste non vérifiée tant que son propre lien n'a pas été suivi ;
/// les liens envoyés à l'ancienne adresse cessent de fonctionner.
/// Avec <paramref name="RequireCurrentPassword"/> (libre-service), le mot de passe actuel doit être confirmé : un access
/// token volé ne doit pas suffire à rediriger les emails du compte, puis à réinitialiser son mot de passe.
/// </summary>
public sealed record ChangeEmailCommand(
    Guid UserId, string? NewEmail, string? CurrentPassword = null, bool RequireCurrentPassword = false) : ICommand;

public sealed class ChangeEmailHandler(
    IUserRepository users,
    IUserEmailUniquenessChecker emailUniqueness,
    PasswordVerifier passwords,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<ChangeEmailCommand>
{
    public async Task HandleAsync(ChangeEmailCommand command, CancellationToken cancellationToken = default)
    {
        var newEmail = Email.Create(command.NewEmail);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        if (command.RequireCurrentPassword)
        {
            await passwords.EnsureAsync(user.Email, user.PasswordHash, command.CurrentPassword, cancellationToken);
        }

        var isUnchanged = newEmail == user.Email;
        var isEmailUnique = isUnchanged || await emailUniqueness.IsUniqueAsync(newEmail, user.Id, cancellationToken);
        user.ChangeEmail(newEmail, isEmailUnique, clock);
        if (isUnchanged)
        {
            return;
        }

        // Un compte invité n'a pas encore de mot de passe : son lien reste une invitation, vers l'adresse corrigée.
        var purpose = user.HasPassword ? UserTokenPurpose.EmailVerification : UserTokenPurpose.Invitation;
        var lifetime = user.HasPassword ? settings.EmailVerificationTokenLifetime : settings.InvitationTokenLifetime;
        var token = await tokens.IssueAsync(user.Id, purpose, clock.UtcNow + lifetime, cancellationToken);
        await tokens.RevokeAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (user.HasPassword)
        {
            await notifier.SendEmailVerificationAsync(newEmail, token, cancellationToken);
        }
        else
        {
            await notifier.SendInvitationAsync(newEmail, token, cancellationToken);
        }
    }
}

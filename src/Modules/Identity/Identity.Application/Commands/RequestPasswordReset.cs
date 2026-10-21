using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// « Mot de passe oublié ». Réussit toujours : la réponse n'indique jamais si le compte existe. Un compte invité qui
/// n'a pas encore de mot de passe reçoit à la place une nouvelle fois son invitation.
/// </summary>
public sealed record RequestPasswordResetCommand(string? Email) : ICommand;

public sealed class RequestPasswordResetHandler(
    IUserRepository users,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken = default)
    {
        var email = Email.Create(command.Email);
        var user = await users.FindByEmailAsync(email, cancellationToken);
        if (user == null || user.Status == UserStatus.Disabled)
        {
            return;
        }

        // Une adresse adoptée par le compte mais jamais prouvée ne doit pas permettre d'en prendre le contrôle. Un
        // compte qui n'a jamais prouvé aucune adresse le peut : le vrai titulaire de la boîte le récupère ainsi.
        var neverActivated = user.Status == UserStatus.Pending
            || (user.Status == UserStatus.Locked && user.StatusBeforeLock == UserStatus.Pending);
        if (user.HasPassword && !user.IsEmailVerified && !neverActivated)
        {
            return;
        }

        var purpose = user.HasPassword ? UserTokenPurpose.PasswordReset : UserTokenPurpose.Invitation;
        var lifetime = user.HasPassword ? settings.PasswordResetTokenLifetime : settings.InvitationTokenLifetime;
        var token = await tokens.IssueAsync(user.Id, purpose, clock.UtcNow + lifetime, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // Le compte a changé, ou une autre requête envoie un lien, à cet instant précis : rester silencieux.
            return;
        }

        if (user.HasPassword)
        {
            await notifier.SendPasswordResetAsync(email, token, cancellationToken);
        }
        else
        {
            await notifier.SendInvitationAsync(email, token, cancellationToken);
        }
    }
}

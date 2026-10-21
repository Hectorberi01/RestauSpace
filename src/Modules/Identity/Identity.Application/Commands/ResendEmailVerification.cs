using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Envoie un nouveau lien de vérification. Réussit toujours : la réponse ne dit pas si le compte existe.</summary>
public sealed record ResendEmailVerificationCommand(string? Email) : ICommand;

public sealed class ResendEmailVerificationHandler(
    IUserRepository users,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<ResendEmailVerificationCommand>
{
    public async Task HandleAsync(ResendEmailVerificationCommand command, CancellationToken cancellationToken = default)
    {
        var email = Email.Create(command.Email);
        var user = await users.FindByEmailAsync(email, cancellationToken);
        if (user == null || user.IsEmailVerified || !user.HasPassword || user.Status == UserStatus.Disabled)
        {
            return;
        }

        var token = await tokens.IssueAsync(
            user.Id, UserTokenPurpose.EmailVerification, clock.UtcNow + settings.EmailVerificationTokenLifetime, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // Le compte a changé, ou une autre requête envoie un lien, à cet instant précis : on reste silencieux.
            return;
        }

        await notifier.SendEmailVerificationAsync(email, token, cancellationToken);
    }
}

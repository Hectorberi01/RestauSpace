using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>
/// Vérifie l'email ACTUEL du compte à partir du lien envoyé par email. Le mot de passe est requis : seul le créateur
/// du compte peut l'activer. Le propriétaire d'une boîte mail utilisée par quelqu'un d'autre pour s'inscrire ne le
/// peut pas, et récupère plutôt le compte via « mot de passe oublié ».
/// </summary>
public sealed record VerifyEmailCommand(string? Token, string? Password) : ICommand;

public sealed class VerifyEmailHandler(
    IUserRepository users, IUserTokens tokens, PasswordVerifier passwords, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task HandleAsync(VerifyEmailCommand command, CancellationToken cancellationToken = default)
    {
        var userId = await tokens.ConsumeAsync(
            command.Token, UserTokenPurpose.EmailVerification, clock.UtcNow, cancellationToken);
        var user = userId is { } id ? await users.FindAsync(id, cancellationToken) : null;
        if (user == null)
        {
            throw new DomainException(IdentityApplicationErrors.TokenInvalid);
        }

        // Un mauvais mot de passe laisse le lien utilisable : rien n'est enregistré.
        await passwords.EnsureAsync(user.Email, user.PasswordHash, command.Password, cancellationToken);
        user.VerifyEmail(clock);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

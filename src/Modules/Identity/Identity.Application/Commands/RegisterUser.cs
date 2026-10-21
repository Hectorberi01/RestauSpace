using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Inscription en libre-service. Le compte reste Pending jusqu'au clic sur le lien reçu par email.</summary>
public sealed record RegisterUserCommand(string? Email, string? Password) : ICommand<Guid>;

public sealed class RegisterUserHandler(
    IUserRepository users,
    IUserEmailUniquenessChecker emailUniqueness,
    IPasswordHasher passwords,
    ICompromisedPasswords compromised,
    IUserTokens tokens,
    IIdentityNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Guid> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken = default)
    {
        var email = Email.Create(command.Email);
        var password = await PasswordPolicy.CheckAsync(command.Password, compromised, cancellationToken);
        var isEmailUnique = await emailUniqueness.IsUniqueAsync(email, null, cancellationToken);

        var user = User.Register(email, passwords.Hash(password), isEmailUnique, clock);
        users.Add(user);
        var token = await tokens.IssueAsync(
            user.Id, UserTokenPurpose.EmailVerification, clock.UtcNow + settings.EmailVerificationTokenLifetime, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notifier.SendEmailVerificationAsync(email, token, cancellationToken);

        return user.Id.Value;
    }
}

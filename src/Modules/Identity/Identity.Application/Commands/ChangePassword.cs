using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// L'utilisateur connecté change son mot de passe. Toutes les autres sessions sont révoquées ;
/// <paramref name="CurrentSessionId"/>, la session d'où vient la requête, est conservée.
/// </summary>
public sealed record ChangePasswordCommand(Guid UserId, string? CurrentPassword, string? NewPassword, Guid? CurrentSessionId = null)
    : ICommand;

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordHasher passwords,
    ICompromisedPasswords compromised,
    PasswordVerifier currentPassword,
    IUserTokens tokens,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        var newPassword = await PasswordPolicy.CheckAsync(command.NewPassword, compromised, cancellationToken);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        await currentPassword.EnsureAsync(user.Email, user.PasswordHash, command.CurrentPassword, cancellationToken);

        user.ChangePassword(passwords.Hash(newPassword), clock, LiveSession(user, command.CurrentSessionId));
        await tokens.RevokeAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// La session à conserver, d'après l'id que porte un access token. Ce jeton peut être antérieur à un refresh : suivre
    /// les rotations jusqu'à la session vivante aujourd'hui, ou n'en garder aucune si la chaîne a disparu.
    /// </summary>
    private SessionId? LiveSession(User user, Guid? sessionId)
    {
        var session = user.Sessions.SingleOrDefault(candidate => candidate.Id.Value == sessionId);
        while (session is { ReplacedBy: { } successor })
        {
            session = user.Sessions.SingleOrDefault(candidate => candidate.Id == successor);
        }

        return session is not null && session.IsUsableAt(clock.UtcNow) ? session.Id : null;
    }
}

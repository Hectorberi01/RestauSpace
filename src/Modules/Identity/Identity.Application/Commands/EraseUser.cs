using Identity.Application.Abstractions;

namespace Identity.Application.Commands;

/// <summary>
/// Efface définitivement les données personnelles d'un compte : email, mot de passe, sessions, liens, et ce que le
/// module en conservait ailleurs. L'id reste, désactivé. En libre-service, le mot de passe actuel est exigé et le
/// seul propriétaire actif d'un restaurant doit d'abord en transmettre la propriété ; le back-office n'est pas bloqué
/// (demande d'effacement à traiter quoi qu'il arrive).
/// </summary>
public sealed record EraseUserCommand(Guid UserId, string? CurrentPassword = null, bool RequireCurrentPassword = false) : ICommand;

public sealed class EraseUserHandler(
    IUserRepository users,
    PasswordVerifier passwords,
    IUserTokens tokens,
    IUserTraces traces,
    ITenantMemberships memberships,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<EraseUserCommand>
{
    public async Task HandleAsync(EraseUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        if (command.RequireCurrentPassword)
        {
            await passwords.EnsureAsync(user.Email, user.PasswordHash, command.CurrentPassword, cancellationToken);
            var owned = await memberships.SoleOwnerTenantsAsync(user.Id, cancellationToken);
            DomainException.ThrowIf(owned.Count > 0, IdentityApplicationErrors.SoleOwner);
        }

        var previousEmail = user.Email;
        user.Erase(Email.Create($"erased-{user.Id.Value:N}@erased.invalid"), clock);
        foreach (var purpose in Enum.GetValues<UserTokenPurpose>())
        {
            await tokens.RevokeAsync(user.Id, purpose, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Après l'enregistrement : les événements écrits jusqu'ici portent l'adresse et doivent disparaître aussi.
        await traces.ForgetAsync(user.Id, previousEmail, cancellationToken);
    }
}

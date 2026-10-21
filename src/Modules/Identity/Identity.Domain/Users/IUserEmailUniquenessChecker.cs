namespace Identity.Domain.Users;

/// <summary>
/// Port qui répond à « cet email est-il libre ? » sur tous les utilisateurs (toute la plateforme).
/// <paramref name="excludedUserId"/> est l'utilisateur qui change son propre email. L'index unique en base reste
/// le dernier rempart contre les accès concurrents.
/// </summary>
public interface IUserEmailUniquenessChecker
{
    Task<bool> IsUniqueAsync(Email email, UserId? excludedUserId, CancellationToken cancellationToken = default);
}

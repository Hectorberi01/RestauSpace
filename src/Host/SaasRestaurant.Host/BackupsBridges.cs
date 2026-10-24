using Backups.Application.Abstractions;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

/// <summary>
/// Backups -> Identity : le code de validation en deux étapes de l'administrateur, avec les limites d'essais d'Identity
/// (trop d'échecs : 429). Un code juste est consommé (pas TOTP ou code de secours).
/// </summary>
internal sealed class AdminTwoFactorFromIdentity(IUserRepository users, TwoFactorVerifier codes, IIdentityUnitOfWork unitOfWork) : IAdminTwoFactor
{
    public async Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || await users.FindAsync(UserId.From(userId), cancellationToken) is not { IsTwoFactorEnabled: true } user)
        {
            return false;
        }

        try
        {
            await codes.EnsureAsync(user, code, cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthenticatedException or DomainException)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

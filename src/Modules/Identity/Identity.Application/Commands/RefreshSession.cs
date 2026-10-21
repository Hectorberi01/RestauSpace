using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Échange un refresh token contre une nouvelle paire. Le jeton présenté cesse de fonctionner ; le présenter à
/// nouveau plus de 10 s après est traité comme un vol et révoque toutes les sessions de l'utilisateur.
/// </summary>
/// <param name="TenantId">
/// Restaurant pour lequel agit le nouvel access token (le client envoie celui qu'il avait sélectionné). Revérifié à
/// chaque refresh : un membre retiré d'un restaurant en perd l'accès en moins d'une durée de vie d'access token.
/// </param>
/// <param name="AfterConflict">
/// La requête est rejouée par le serveur après avoir perdu une course. Si le gagnant a fait tourner ce même jeton, les
/// deux requêtes venaient du même client au même moment : c'est un conflit, pas un vol.
/// </param>
public sealed record RefreshSessionCommand(string? RefreshToken, bool AfterConflict = false, Guid? TenantId = null)
    : ICommand<AuthTokensDto>;

public sealed class RefreshSessionHandler(
    IUserRepository users,
    IRefreshTokens refreshTokens,
    IAccessTokenIssuer accessTokens,
    ITenantMemberships memberships,
    IIdentityUnitOfWork unitOfWork,
    IdentitySettings settings,
    IClock clock)
    : ICommandHandler<RefreshSessionCommand, AuthTokensDto>
{
    public static readonly TimeSpan ConcurrentRefreshWindow = TimeSpan.FromSeconds(10);

    public async Task<AuthTokensDto> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            throw new UnauthenticatedException(IdentityApplicationErrors.InvalidRefreshToken);
        }

        var presented = refreshTokens.Hash(command.RefreshToken);
        var user = await users.FindBySessionTokenAsync(presented, cancellationToken);
        if (user == null)
        {
            throw new UnauthenticatedException(IdentityApplicationErrors.InvalidRefreshToken);
        }

        // Même jeton envoyé deux fois coup sur coup (double envoi, nouvelle tentative réseau) : conflit, pas un vol.
        // Au-delà de ConcurrentRefreshWindow, le rejeu révoque toutes les sessions.
        var rotated = user.Sessions.SingleOrDefault(session => session.Hash == presented && session.WasRotated);
        if (rotated is not null && (command.AfterConflict || rotated.RevokedAtUtc >= clock.UtcNow - ConcurrentRefreshWindow))
        {
            throw new ConcurrencyException(IdentityApplicationErrors.ConcurrentUpdate);
        }

        var next = refreshTokens.Generate();
        SessionRotationResult result;
        try
        {
            result = user.RotateSession(presented, next.Hash, settings.SessionLifetime, clock);
        }
        catch (DomainException refused)
        {
            // Session révoquée ou expirée, compte verrouillé ou désactivé : l'appelant doit se reconnecter.
            throw new UnauthenticatedException(refused.Error);
        }

        // En cas de rejeu, l'agrégat a révoqué toutes les sessions : à enregistrer AVANT de refouler l'appelant.
        if (result.ReplayDetected)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthenticatedException(result.Error);
        }

        // Vérifié après la détection de rejeu (un jeton volé ne doit pas y échapper via un tenant inaccessible) et avant
        // l'enregistrement : en cas de refus, la rotation n'est pas gardée et le refresh token présenté reste valide.
        TenantId? tenantId = null;
        if (command.TenantId is { } requested)
        {
            if (requested == Guid.Empty
                || !await memberships.CanActForTenantAsync(user.Id, TenantId.From(requested), cancellationToken))
            {
                throw new ForbiddenException(IdentityApplicationErrors.TenantAccessDenied);
            }

            tenantId = TenantId.From(requested);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AuthTokensDto.From(user, result.NewSession, accessTokens.Issue(user, result.NewSession.Id, tenantId), next);
    }
}

using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>
/// Donne à l'utilisateur connecté un access token qui agit pour l'un de ses restaurants (claim <c>tid</c>). Le tenant
/// vient de la requête mais n'est écrit dans le token qu'une fois l'appartenance vérifiée ; toute route agissant pour
/// un restaurant le lit ensuite dans le token, jamais dans la requête.
/// </summary>
public sealed record SelectTenantCommand(Guid UserId, Guid? SessionId, Guid TenantId) : ICommand<TenantAccessTokenDto>;

public sealed record TenantAccessTokenDto(Guid TenantId, string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc);

public sealed class SelectTenantHandler(IUserRepository users, ITenantMemberships memberships, IAccessTokenIssuer accessTokens)
    : ICommandHandler<SelectTenantCommand, TenantAccessTokenDto>
{
    public async Task<TenantAccessTokenDto> HandleAsync(SelectTenantCommand command, CancellationToken cancellationToken = default)
    {
        if (command.UserId == Guid.Empty || command.SessionId is not { } sessionId || sessionId == Guid.Empty)
        {
            throw new UnauthenticatedException(IdentityApplicationErrors.InvalidAccessToken);
        }

        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken)
            ?? throw new UnauthenticatedException(IdentityApplicationErrors.InvalidAccessToken);

        // Tenant inconnu et tenant d'un autre reçoivent la même réponse : elle ne doit pas révéler lesquels existent.
        if (command.TenantId == Guid.Empty
            || !await memberships.CanActForTenantAsync(user.Id, TenantId.From(command.TenantId), cancellationToken))
        {
            throw new ForbiddenException(IdentityApplicationErrors.TenantAccessDenied);
        }

        var tenantId = TenantId.From(command.TenantId);
        var accessToken = accessTokens.Issue(user, SessionId.From(sessionId), tenantId);
        return new TenantAccessTokenDto(tenantId.Value, accessToken.Value, accessToken.ExpiresAtUtc);
    }
}

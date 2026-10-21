using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    /// <param name="tenantId">Restaurant pour lequel agit le token, déjà vérifié par l'appelant ;
    /// null = aucun restaurant.</param>
    AccessToken Issue(User user, SessionId sessionId, TenantId? tenantId = null);
}

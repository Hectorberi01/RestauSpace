using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>Session de caisse partagée : le jeton est marqué (caisse, établissement) et expire avec la session.</summary>
public sealed record TillAccess(Guid TillId, Guid LocationId, DateTimeOffset ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    /// <param name="tenantId">Restaurant pour lequel agit le token, déjà vérifié par l'appelant ;
    /// null = aucun restaurant.</param>
    /// <param name="till">Session de caisse partagée ; null pour une session ordinaire.</param>
    AccessToken Issue(User user, SessionId sessionId, TenantId? tenantId = null, TillAccess? till = null);
}

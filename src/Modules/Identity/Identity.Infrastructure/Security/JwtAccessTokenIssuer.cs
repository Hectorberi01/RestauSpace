using System.Security.Claims;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

/// <summary>
/// JWT signé : <c>sub</c> = id utilisateur, <c>sid</c> = id de session, <c>tid</c> = tenant sélectionné (seulement une
/// fois l'appartenance vérifiée), <c>till</c>/<c>till_loc</c> pour une session de caisse. Aucun rôle : les permissions
/// ne relèvent pas d'Identity.
/// </summary>
internal sealed class JwtAccessTokenIssuer(AccessTokenOptions options, IClock clock) : IAccessTokenIssuer
{
    private readonly SigningCredentials _credentials =
        new(new SymmetricSecurityKey(options.SigningKeyBytes()), SecurityAlgorithms.HmacSha256);

    public AccessToken Issue(User user, SessionId sessionId, TenantId? tenantId = null, TillAccess? till = null)
    {
        var now = clock.UtcNow;
        var expiresAtUtc = till?.ExpiresAtUtc ?? now.Add(options.Lifetime);
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Sid, sessionId.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email.Value),
            // Qui accorde des droits d'après l'email doit vérifier ceci : une adresse peut changer avant d'être prouvée.
            new Claim(JwtRegisteredClaimNames.EmailVerified, user.IsEmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        ];
        if (user.IsTwoFactorEnabled)
        {
            claims.Add(new Claim("tfa", "true"));
        }

        if (tenantId is { } tenant)
        {
            claims.Add(new Claim(TenantClaims.TenantId, tenant.Value.ToString()));
        }

        if (till is not null)
        {
            claims.Add(new Claim(TillClaims.TillId, till.TillId.ToString()));
            claims.Add(new Claim(TillClaims.LocationId, till.LocationId.ToString()));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = _credentials,
        });

        return new AccessToken(token, expiresAtUtc);
    }
}

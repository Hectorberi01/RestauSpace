using System.Security.Claims;
using Identity.Application;

namespace Identity.Api;

public static class IdentityClaims
{
    public const string UserId = "sub";
    public const string SessionId = "sid";
    public const string Email = "email";
    public const string EmailVerified = "email_verified";

    /// <summary>« true » si le compte a la validation en deux étapes (chaque connexion a donc demandé le code).</summary>
    public const string TwoFactor = "tfa";
    public const string TenantId = TenantClaims.TenantId;

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id)
            ? id
            : throw new UnauthenticatedException(IdentityApplicationErrors.InvalidAccessToken);

    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionId), out var id) ? id : null;
}

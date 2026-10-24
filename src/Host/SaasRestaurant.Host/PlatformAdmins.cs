using System.Security.Claims;
using Identity.Api;

namespace SaasRestaurant.Host;

/// <summary>
/// Administrateur de la plateforme : email vérifié et déclaré, jeton sans restaurant (une session de caisse, ouverte par
/// un simple code, n'y donne jamais accès) et, si exigé, compte avec la validation en deux étapes.
/// </summary>
internal sealed class PlatformAdmins(IEnumerable<string> emails, bool requireTwoFactor = true)
{
    private readonly HashSet<string> _emails = emails
        .Where(email => !string.IsNullOrWhiteSpace(email))
        .Select(email => email.Trim().ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);

    public bool Contains(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(IdentityClaims.EmailVerified), "true", StringComparison.OrdinalIgnoreCase)
        && user.FindFirstValue(IdentityClaims.Email) is { } email
        && _emails.Contains(email)
        && user.FindFirstValue(IdentityClaims.TenantId) is null
        && (!requireTwoFactor || string.Equals(user.FindFirstValue(IdentityClaims.TwoFactor), "true", StringComparison.OrdinalIgnoreCase));
}

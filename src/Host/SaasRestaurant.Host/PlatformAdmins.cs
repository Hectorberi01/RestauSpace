using System.Security.Claims;
using Identity.Api;

namespace SaasRestaurant.Host;

internal sealed class PlatformAdmins(IEnumerable<string> emails)
{
    private readonly HashSet<string> _emails = emails
        .Where(email => !string.IsNullOrWhiteSpace(email))
        .Select(email => email.Trim().ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);

    public bool Contains(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(IdentityClaims.EmailVerified), "true", StringComparison.OrdinalIgnoreCase)
        && user.FindFirstValue(IdentityClaims.Email) is { } email
        && _emails.Contains(email);
}

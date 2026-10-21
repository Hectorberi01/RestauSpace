using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Api;

public sealed class AuthRateLimitOptions
{
    public int PermitLimit { get; set; } = 60;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static class IdentityRateLimiting
{
    public const string AuthPolicy = "identity-auth";

    /// <summary>
    /// Par adresse client, sur l'ensemble des routes <c>/auth</c>. L'hôte doit appeler <c>app.UseRateLimiter()</c> et,
    /// derrière un proxy, rétablir la vraie adresse client (forwarded headers) : sinon tous les appelants partagent
    /// un même budget.
    /// </summary>
    internal static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, AuthRateLimitOptions options) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = options.Window,
                    QueueLimit = 0,
                }));
        });
}

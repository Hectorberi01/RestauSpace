using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Api;

public static class NotificationsRateLimiting
{
    public const string ConfigurationTestPolicy = "notifications-configuration-test";

    /// <summary>
    /// Messages de test : 10 par tranche de 10 minutes et par utilisateur (adresse client à défaut), pour qu'une console
    /// ne serve pas de relais d'envoi. L'hôte appelle <c>app.UseRateLimiter()</c>.
    /// </summary>
    internal static IServiceCollection AddConfigurationTestRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(ConfigurationTestPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        });
}

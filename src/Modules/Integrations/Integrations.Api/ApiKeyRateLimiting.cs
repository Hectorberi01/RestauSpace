using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;

namespace Integrations.Api;

/// <summary>Débit autorisé à CHAQUE clé d'API (section <c>Integrations:ApiKeyRateLimit</c> de l'hôte).</summary>
public sealed class ApiKeyRateLimitOptions
{
    /// <summary>Requêtes autorisées par fenêtre glissante.</summary>
    public int PermitLimit { get; set; } = 600;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Découpage de la fenêtre glissante (6 segments de 10 s pour une minute).</summary>
    public int SegmentsPerWindow { get; set; } = 6;
}

/// <summary>
/// Limite de débit PAR CLÉ D'API (claim <c>cid</c>), en mémoire de l'instance : avec plusieurs instances de l'hôte, chacune
/// accorde ce débit. Les requêtes des utilisateurs (access token) ne sont pas concernées. Au-delà : 429 avec
/// <c>Retry-After</c>. L'hôte doit appeler <c>app.UseRateLimiter()</c> APRÈS <c>UseAuthentication</c>.
/// </summary>
public static class ApiKeyRateLimiting
{
    public static IServiceCollection AddIntegrationsApiKeyRateLimit(this IServiceCollection services, Action<ApiKeyRateLimitOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new ApiKeyRateLimitOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PermitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SegmentsPerWindow, 1);

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                ApiKeyClaims.IsApiKey(context.User) && context.User.FindFirst(ApiKeyClaims.CredentialId)?.Value is { Length: > 0 } credentialId
                    ? RateLimitPartition.GetSlidingWindowLimiter(credentialId, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        SegmentsPerWindow = options.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                    : RateLimitPartition.GetNoLimiter(string.Empty));

            var previous = limiter.OnRejected;
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    rejected.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                if (previous is not null)
                {
                    await previous(rejected, cancellationToken);
                }
            };
        });
    }
}

using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Api;

/// <summary>
/// Deux requêtes sur un même compte au même instant (deux appareils qui se connectent, un refresh pendant une
/// déconnexion) : la perdante du contrôle de concurrence optimiste est rejouée ici sur des données fraîches au lieu
/// de recevoir un 409. Avec N requêtes simultanées, une requête perd au plus N-1 fois ; au-delà de
/// <see cref="MaxAttempts"/>, le 409 part.
/// </summary>
internal static class ConflictRetry
{
    public const int MaxAttempts = 5;

    private const string AttemptKey = "identity.conflict-retry";

    public static bool IsRetry(this HttpContext httpContext) => httpContext.Items.ContainsKey(AttemptKey);

    public static RouteGroupBuilder RetryOnConflict(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (invocation, next) =>
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await next(invocation);
                }
                catch (ConcurrencyException) when (attempt < MaxAttempts)
                {
                    var httpContext = invocation.HttpContext;
                    // Oublie ce que cette requête a chargé : le handler s'exécute à nouveau comme pour la première fois.
                    httpContext.RequestServices.GetRequiredService<IdentityDbContext>().ChangeTracker.Clear();
                    httpContext.Items[AttemptKey] = attempt;
                    await Task.Delay(Random.Shared.Next(5, 40), httpContext.RequestAborted);
                }
            }
        });
        return group;
    }
}

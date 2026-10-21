using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace SaasRestaurant.BuildingBlocks.Api;

public static class Tenancy
{
    /// <summary>Policy des routes agissant pour un restaurant : un utilisateur connecté qui a choisi un tenant.</summary>
    public const string TenantMemberPolicy = "tenant-member";

    /// <summary>Fait de <see cref="ITenantContext"/> le tenant de l'access token de la requête.</summary>
    public static IServiceCollection AddRequestTenantContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext, ClaimsTenantContext>();
        return services;
    }

    /// <summary>
    /// Ajoute <see cref="TenantMemberPolicy"/>. L'appartenance elle-même a été vérifiée quand le tenant a été mis dans
    /// le token, et l'est de nouveau à chaque refresh : le claim suffit ici.
    /// </summary>
    public static AuthorizationBuilder AddTenantMemberPolicy(this AuthorizationBuilder authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return authorization.AddPolicy(TenantMemberPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.TenantId));
    }

    /// <summary>
    /// Chaque log écrit pendant la requête porte TenantId, UserId et CorrelationId (aucune autre donnée personnelle).
    /// À placer après <c>UseAuthentication</c>.
    /// </summary>
    public static IApplicationBuilder UseRequestLogScope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SaasRestaurant.Request");
            var scope = new Dictionary<string, object?>
            {
                ["CorrelationId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                ["TenantId"] = context.User.FindFirst(TenantClaims.TenantId)?.Value,
                ["UserId"] = context.User.FindFirst("sub")?.Value,
            };
            using (logger.BeginScope(scope))
            {
                await next(context);
            }
        });
    }
}

/// <summary>
/// Pendant une requête : le claim <c>tid</c> de l'access token validé (pas de claim, ou un claim mal formé : pas de
/// tenant). Hors requête (consommateur d'outbox, job) : le <see cref="BackgroundTenant"/> du scope, s'il a été fixé.
/// </summary>
internal sealed class ClaimsTenantContext(IHttpContextAccessor accessor, BackgroundTenant background) : ITenantContext
{
    public TenantId? CurrentTenantId
    {
        get
        {
            if (accessor.HttpContext is not { } http)
            {
                return background.Current;
            }

            return Guid.TryParse(http.User.FindFirst(TenantClaims.TenantId)?.Value, out var id) && id != Guid.Empty
                ? TenantId.From(id)
                : null;
        }
    }
}

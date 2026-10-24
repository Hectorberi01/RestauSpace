using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

/// <summary>
/// Session de caisse partagée (ouverte par PIN, claims <see cref="TillClaims"/>) : seules les permissions de la liste
/// blanche passent, dans l'établissement de la caisse ; une route sans permission n'est ouverte qu'en lecture (GET),
/// sauf métadonnée <see cref="AllowTillSession{TBuilder}"/> ou <see cref="DenyTillSession{TBuilder}"/>.
/// </summary>
public static class TillSessions
{
    public static readonly DomainError Forbidden =
        new("common.till_session.forbidden", "This action needs a personal session, not a shared till session.");

    public static bool IsTillSession(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.HasClaim(claim => claim.Type == TillClaims.TillId);
    }

    /// <summary>Établissement de la caisse ; null hors session de caisse (ou claim illisible).</summary>
    public static Guid? LocationOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Guid.TryParse(user.FindFirst(TillClaims.LocationId)?.Value, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>Liste blanche des permissions qu'une session de caisse peut exercer (sans elle : aucune).</summary>
    public static IServiceCollection AddTillSessionPermissions(this IServiceCollection services, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        services.AddSingleton(new TillSessionPermissions(permissions));
        return services;
    }

    /// <summary>Ouvre à une session de caisse une route d'écriture sans permission propre.</summary>
    public static TBuilder AllowTillSession<TBuilder>(this TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder =>
        endpoint.WithMetadata(new TillSessionAccess(true));

    /// <summary>Ferme à une session de caisse une route, même en lecture.</summary>
    public static TBuilder DenyTillSession<TBuilder>(this TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder =>
        endpoint.WithMetadata(new TillSessionAccess(false));

    /// <summary>Vrai si la requête vient d'une session de caisse et que la route lui est fermée.</summary>
    public static bool Refuses(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsTillSession(context.User))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<TillSessionAccess>() is { } access)
        {
            return !access.Allowed;
        }

        var policies = endpoint?.Metadata.GetOrderedMetadata<AuthorizationPolicy>() ?? Array.Empty<AuthorizationPolicy>();
        return !policies.Any(policy => policy.Requirements.OfType<PermissionRequirement>().Any())
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method);
    }

    /// <summary>Applique <see cref="Refuses"/> à toutes les routes. À placer après <c>UseAuthentication</c>.</summary>
    public static IApplicationBuilder UseTillSessionRestrictions(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (Refuses(context))
            {
                throw new ForbiddenException(Forbidden);
            }

            await next(context);
        });
    }

    /// <summary>Même règle, en filtre d'un groupe de routes (garantie du module, indépendante de l'hôte).</summary>
    public static RouteGroupBuilder RestrictTillSessions(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.AddEndpointFilter(async (invocation, next) =>
            Refuses(invocation.HttpContext) ? throw new ForbiddenException(Forbidden) : await next(invocation));
        return group;
    }
}

public sealed record TillSessionAccess(bool Allowed);

/// <summary>Permissions d'exploitation ouvertes à une session de caisse ; tout le reste est refusé.</summary>
public sealed class TillSessionPermissions(IEnumerable<string> permissions)
{
    private readonly HashSet<string> _allowed = permissions.ToHashSet(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Allowed => _allowed;

    public bool Allows(string permission) => _allowed.Contains(permission);

    public IReadOnlyList<string> Filter(IEnumerable<string> permissions) =>
        permissions.Where(_allowed.Contains).ToList();
}

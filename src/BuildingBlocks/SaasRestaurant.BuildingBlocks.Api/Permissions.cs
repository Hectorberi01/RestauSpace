using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

public static class Permissions
{
    /// <summary>
    /// La route exige <paramref name="permission"/> pour l'utilisateur et le tenant de l'access token. Si la requête porte
    /// un établissement (<c>locationId</c> dans la route ou la query), c'est le rôle du membre DANS cet établissement qui
    /// compte (à défaut, son rôle de tenant) ; sinon son rôle de tenant. À combiner avec
    /// <see cref="Tenancy.TenantMemberPolicy"/> sur le groupe. L'hôte peut garder la réponse en cache quelques secondes
    /// (Caching:PermissionSeconds).
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder endpoint, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return endpoint.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.TenantId)
            .AddRequirements(new PermissionRequirement(permission))
            .Build());
    }

    /// <summary>Évalue <see cref="RequirePermission{TBuilder}"/> avec le <see cref="IPermissionChecker"/> enregistré.</summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Les claims d'une identité authentifiée par clé d'API (module Integrations) : le tenant (<c>tid</c>) vient du
/// credential, jamais de la requête ; chaque scope accordé est un claim <see cref="Scope"/>, du même vocabulaire que les
/// codes de permission. Une telle identité n'a pas d'utilisateur (<c>sub</c>).
/// </summary>
public static class ApiKeyClaims
{
    public const string AuthenticationType = "ApiKey";
    public const string Scope = "api_scope";
    public const string CredentialId = "cid";

    public static bool IsApiKey(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == AuthenticationType);
    }

    /// <summary>
    /// Ouvre en LECTURE (GET) aux clés d'API qui portent l'un de ces scopes une route sans permission propre (ex. lire le
    /// catalogue avec le scope <c>catalog.manage</c> ou <c>orders.take</c>). L'accès du personnel ne change pas.
    /// </summary>
    public static TBuilder AllowApiKeyRead<TBuilder>(this TBuilder endpoint, params string[] scopes)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(scopes);
        return endpoint.WithMetadata(new ApiKeyReadAccess(scopes));
    }

    /// <summary>
    /// Une clé d'API n'atteint QUE les routes protégées par <see cref="Permissions.RequirePermission{TBuilder}"/> : toute
    /// autre route (profil, sélection de tenant, routes de la plateforme, routes anonymes) répond 403. À placer après
    /// <c>UseAuthentication</c>.
    /// </summary>
    public static IApplicationBuilder UseApiKeyRestrictions(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (IsApiKey(context.User))
            {
                var endpoint = context.GetEndpoint();
                var policies = endpoint?.Metadata.GetOrderedMetadata<AuthorizationPolicy>() ?? Array.Empty<AuthorizationPolicy>();
                if (!policies.Any(policy => policy.Requirements.OfType<PermissionRequirement>().Any())
                    && !ApiKeyReadAccess.Allows(endpoint?.Metadata.GetMetadata<ApiKeyReadAccess>(), context))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next(context);
        });
    }
}

/// <summary>
/// Refus par défaut : pas de checker enregistré, d'utilisateur ou de tenant dans le token = pas de permission. Une clé
/// d'API a la permission si et seulement si elle porte le scope du même nom (pas de rôle, pas de joker).
/// </summary>
internal sealed class PermissionAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (ApiKeyClaims.IsApiKey(context.User))
        {
            if (context.User.HasClaim(ApiKeyClaims.Scope, requirement.Permission))
            {
                context.Succeed(requirement);
            }

            return;
        }

        var checker = services.GetService<IPermissionChecker>();
        if (checker is null
            || !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty
            || !Guid.TryParse(context.User.FindFirst(TenantClaims.TenantId)?.Value, out var tenantId) || tenantId == Guid.Empty)
        {
            return;
        }

        // Rôle de restaurant seul : un locationId fourni par la requête ne doit jamais choisir le rôle évalué.
        var aborted = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await checker.HasTenantPermissionAsync(UserId.From(userId), TenantId.From(tenantId), requirement.Permission, aborted))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Métadonnée de <see cref="ApiKeyClaims.AllowApiKeyRead{TBuilder}"/> : les scopes qui ouvrent la lecture d'une route.</summary>
public sealed record ApiKeyReadAccess(IReadOnlyList<string> Scopes)
{
    public static bool Allows(ApiKeyReadAccess? access, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return access is not null
            && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && context.User.FindAll(ApiKeyClaims.Scope).Any(claim => access.Scopes.Contains(claim.Value, StringComparer.Ordinal));
    }
}

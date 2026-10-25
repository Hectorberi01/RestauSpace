using System.Security.Claims;
using System.Text.Encodings.Web;
using Integrations.Application.Commands;
using Integrations.Application.Queries;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;

namespace Integrations.Api;

/// <summary>
/// Authentification des intégrations par clé d'API (en-tête <see cref="HeaderName"/>). Le schéma par défaut de l'hôte
/// devient <see cref="SelectorScheme"/> : une requête qui porte l'en-tête est authentifiée par clé, toute autre par
/// access token (<c>Bearer</c>). Ajouter un second schéma supprime le choix automatique d'ASP.NET : l'hôte DOIT fixer
/// le schéma par défaut (<c>AddAuthentication(ApiKeyAuthentication.SelectorScheme)</c>).
/// </summary>
public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string SelectorScheme = "BearerOrApiKey";
    public const string HeaderName = "X-Api-Key";

    public static AuthenticationBuilder AddIntegrationsApiKeys(this AuthenticationBuilder authentication, string tokenScheme = "Bearer")
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenScheme);
        authentication.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
        authentication.AddPolicyScheme(SelectorScheme, "Access token or API key", options =>
            options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(HeaderName) ? Scheme : tokenScheme);
        return authentication;
    }

    /// <summary>
    /// Une requête authentifiée par clé d'API exige l'option API du plan : sinon 403 <c>common.plan.feature_required</c>
    /// (pas 401 : la clé est valide). À placer après <c>UseAuthentication</c>, le tenant est alors dans le contexte.
    /// </summary>
    public static IApplicationBuilder UseApiKeyPlanCheck(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (ApiKeyClaims.IsApiKey(context.User))
            {
                await context.RequestServices.GetRequiredService<ICommandHandler<EnsureApiKeyPlanCommand>>()
                    .HandleAsync(new EnsureApiKeyPlanCommand(), context.RequestAborted);
            }

            await next(context);
        });
    }
}

/// <summary>
/// Retrouve le credential par l'empreinte de la clé ; le tenant (<c>tid</c>) est CELUI DU CREDENTIAL, jamais une valeur
/// de la requête. Réponse unique pour toute clé refusée (inconnue, révoquée, expirée) ; la clé n'apparaît jamais dans
/// un journal ni une exception.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Refused = "Invalid API key.";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthentication.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return AuthenticateResult.Fail(Refused);
        }

        var handler = Context.RequestServices.GetRequiredService<IQueryHandler<AuthenticateApiKeyQuery, ApiKeyPrincipal?>>();
        var principal = await handler.HandleAsync(new AuthenticateApiKeyQuery(values[0]), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail(Refused);
        }

        var claims = new List<Claim>
        {
            new(TenantClaims.TenantId, principal.TenantId.ToString("D")),
            new(ApiKeyClaims.CredentialId, principal.CredentialId.ToString("D")),
        };
        claims.AddRange(principal.Scopes.Select(scope => new Claim(ApiKeyClaims.Scope, scope)));
        var identity = new ClaimsIdentity(claims, ApiKeyClaims.AuthenticationType);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

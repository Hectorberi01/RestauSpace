using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Domain;

namespace Staff.Api;

/// <summary>
/// Authentification d'une caisse partagée par sa clé (en-tête <c>X-Till-Key</c>). Ce schéma n'est JAMAIS le schéma par
/// défaut : seules les routes <c>/till</c> le demandent. Le tenant (<c>tid</c>) est celui de la caisse, jamais une saisie.
/// </summary>
public static class TillKeyAuthentication
{
    public const string Scheme = "TillKey";
    public const string Policy = "Till";
    public const string HeaderName = "X-Till-Key";
    public const string TillClaim = "till";
    public const string RateLimitPolicy = "till";

    public static AuthenticationBuilder AddTillKeys(this AuthenticationBuilder authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        authentication.AddScheme<AuthenticationSchemeOptions, TillKeyAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
        authentication.Services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.TenantId)
            .RequireClaim(TillClaim));
        return authentication;
    }

    /// <summary>60 requêtes par minute et par caisse (ou par adresse si la clé est refusée).</summary>
    internal static IServiceCollection AddTillRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(TillClaim)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
}

/// <summary>Réponse unique pour toute clé refusée (inconnue, mal formée, révoquée) ; la clé n'est jamais journalisée.</summary>
internal sealed class TillKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[TillKeyAuthentication.HeaderName].ToString();
        if (key.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var handler = Context.RequestServices.GetRequiredService<IQueryHandler<AuthenticateTillQuery, TillPrincipal?>>();
        var principal = await handler.HandleAsync(new AuthenticateTillQuery(key), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("Invalid till key.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(TenantClaims.TenantId, principal.TenantId.ToString("D")),
                new Claim(TillKeyAuthentication.TillClaim, principal.TillId.ToString("D")),
            ],
            TillKeyAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>La caisse de la requête : seulement si la requête a été authentifiée par sa clé.</summary>
internal sealed class HttpTillClient(IHttpContextAccessor accessor) : ITillClient
{
    public TillId? TillId =>
        accessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == TillKeyAuthentication.Scheme) == true
        && Guid.TryParse(accessor.HttpContext.User.FindFirst(TillKeyAuthentication.TillClaim)?.Value, out var id) && id != Guid.Empty
            ? Domain.TillId.From(id)
            : null;
}

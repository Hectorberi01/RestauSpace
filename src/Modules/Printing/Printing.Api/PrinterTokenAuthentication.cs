using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Printing.Application.Abstractions;
using Printing.Application.Commands;
using Printing.Domain;

namespace Printing.Api;

/// <summary>
/// Authentification des imprimantes (et des relais) par le jeton placé dans l'adresse qu'elles interrogent
/// (<c>{printerToken}</c>) : une imprimante Star CloudPRNT ne sait envoyer qu'une adresse. Ce schéma n'est JAMAIS le schéma
/// par défaut : seules les routes des imprimantes le demandent. Le tenant (<c>tid</c>) est celui de l'imprimante.
/// </summary>
public static class PrinterTokenAuthentication
{
    public const string Scheme = "PrinterToken";
    public const string Policy = "Printer";
    public const string RouteValue = "printerToken";
    public const string PrinterClaim = "prn";
    public const string RateLimitPolicy = "printer";

    public static AuthenticationBuilder AddPrinterTokens(this AuthenticationBuilder authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        authentication.AddScheme<AuthenticationSchemeOptions, PrinterTokenAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
        authentication.Services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.TenantId)
            .RequireClaim(PrinterClaim));
        return authentication;
    }

    /// <summary>120 requêtes par minute par imprimante (une imprimante CloudPRNT interroge toutes les 5 à 10 secondes).</summary>
    public static IServiceCollection AddPrinterRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(PrinterClaim)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
}

/// <summary>Réponse unique pour tout jeton refusé (inconnu, imprimante coupée, restaurant suspendu) ; le jeton n'est jamais journalisé.</summary>
internal sealed class PrinterTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetRouteValue(PrinterTokenAuthentication.RouteValue) is not string token || token.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var handler = Context.RequestServices.GetRequiredService<IQueryHandler<AuthenticatePrinterQuery, PrinterPrincipal?>>();
        var principal = await handler.HandleAsync(new AuthenticatePrinterQuery(token), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("Invalid printer token.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(TenantClaims.TenantId, principal.TenantId.ToString("D")),
                new Claim(PrinterTokenAuthentication.PrinterClaim, principal.PrinterId.ToString("D")),
            ],
            PrinterTokenAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>L'imprimante de la requête : seulement si la requête a été authentifiée par son jeton.</summary>
internal sealed class HttpPrinterClient(IHttpContextAccessor accessor) : IPrinterClient
{
    public PrinterId? PrinterId =>
        accessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == PrinterTokenAuthentication.Scheme) == true
        && Guid.TryParse(accessor.HttpContext.User.FindFirst(PrinterTokenAuthentication.PrinterClaim)?.Value, out var id) && id != Guid.Empty
            ? Domain.PrinterId.From(id)
            : null;
}

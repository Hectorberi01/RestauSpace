using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Storefront.Application.Abstractions;
using Storefront.Application.Commands;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Api;

/// <summary>
/// Authentification des applications de vente par clé publique (en-tête <see cref="HeaderName"/>). Ce schéma n'est
/// JAMAIS le schéma par défaut : seules les routes <c>/storefront</c> le demandent (politique <see cref="Policy"/>), donc
/// une clé publique n'ouvre aucune autre route, et un jeton du personnel n'ouvre pas celles-ci.
/// </summary>
public static class StorefrontKeyAuthentication
{
    public const string Scheme = "StorefrontKey";
    public const string HeaderName = "X-Storefront-Key";

    /// <summary>Jeton du widget anti-robot (Turnstile), exigé par les routes sensibles quand l'application l'a activé.</summary>
    public const string HumanCheckHeaderName = "X-Human-Check";
    public const string Policy = "StorefrontClient";
    public const string CorsPolicy = "storefront";
    public const string RateLimitPolicy = "storefront";
    public const string AppClaim = "sfa";
    public const string ChannelClaim = "sfc";
    public const string DeviceClaim = "sfd";
    public const string DeviceLocationClaim = "sfl";
    public const string DeviceHeaderName = Storefront.Application.Kiosks.KioskSecrets.HeaderName;
    public const string CustomerHeaderName = Storefront.Application.Customers.CustomerSecrets.HeaderName;
    public const string CustomerSessionClaim = "sfs";
    public const string CustomerClaim = "sfu";
    public const string CodeRateLimitPolicy = "storefront-code";

    public static AuthenticationBuilder AddStorefrontKeys(this AuthenticationBuilder authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        authentication.AddScheme<AuthenticationSchemeOptions, StorefrontKeyAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
        authentication.Services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(TenantClaims.TenantId)
            .RequireClaim(AppClaim));
        return authentication;
    }

    /// <summary>
    /// CORS des routes publiques : toute origine pour la requête préalable (elle ne porte pas la clé), aucun cookie ni
    /// identifiant. Le vrai contrôle est fait à l'authentification : une clé de site web n'est acceptée que depuis ses
    /// origines déclarées.
    /// </summary>
    public static IServiceCollection AddStorefrontCors(this IServiceCollection services) =>
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .SetIsOriginAllowed(_ => true)
            .WithMethods("GET", "POST")
            .WithHeaders(HeaderName, DeviceHeaderName, CustomerHeaderName, HumanCheckHeaderName, "Content-Type", "Idempotency-Key", "Accept-Language", "If-None-Match")
            .WithExposedHeaders("Retry-After", "Idempotency-Replayed", "ETag", "Location")
            .SetPreflightMaxAge(TimeSpan.FromHours(1))));

    /// <summary>120 requêtes par minute par application et par adresse client.</summary>
    public static IServiceCollection AddStorefrontRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"{http.User.FindFirst(AppClaim)?.Value ?? "-"}|{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Connexion par code : 30 demandes ou vérifications par 10 minutes par application et par adresse (les opérateurs mobiles
            // partagent souvent une adresse entre beaucoup de clients), en plus des limites de Notifications par destinataire.
            limiter.AddPolicy(CodeRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"code|{http.User.FindFirst(AppClaim)?.Value ?? "-"}|{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        });
}

/// <summary>
/// Retrouve l'application par l'empreinte de la clé ; le tenant (<c>tid</c>) est CELUI DE L'APPLICATION, jamais une
/// valeur de la requête. Une borne ajoute son jeton d'appareil (en-tête <c>X-Kiosk-Device</c>) : refusé, il fait refuser
/// toute la requête. Réponse unique pour toute clé refusée (inconnue, désactivée, origine non déclarée, restaurant
/// suspendu, jeton de borne révoqué) ; ni la clé ni le jeton n'apparaissent dans un journal ou une exception.
/// </summary>
internal sealed class StorefrontKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Refused = "Invalid publishable key.";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(StorefrontKeyAuthentication.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return AuthenticateResult.Fail(Refused);
        }

        string? deviceToken = null;
        if (Request.Headers.TryGetValue(StorefrontKeyAuthentication.DeviceHeaderName, out var devices))
        {
            if (devices.Count != 1 || string.IsNullOrEmpty(devices[0]))
            {
                return AuthenticateResult.Fail(Refused);
            }

            deviceToken = devices[0];
        }

        // Jeton client illisible ou en double : la requête reste celle d'un invité.
        var customerToken = Request.Headers.TryGetValue(StorefrontKeyAuthentication.CustomerHeaderName, out var customers) && customers.Count == 1
            ? customers[0]
            : null;
        var origin = Request.Headers.Origin.Count == 1 ? Request.Headers.Origin[0] : null;
        var handler = Context.RequestServices.GetRequiredService<IQueryHandler<AuthenticatePublishableKeyQuery, StorefrontKeyPrincipal?>>();
        var principal = await handler.HandleAsync(new AuthenticatePublishableKeyQuery(values[0], origin, deviceToken, customerToken), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail(Refused);
        }

        var claims = new List<Claim>
        {
            new(TenantClaims.TenantId, principal.TenantId.ToString("D")),
            new(StorefrontKeyAuthentication.AppClaim, principal.AppId.ToString("D")),
            new(StorefrontKeyAuthentication.ChannelClaim, principal.Kind.ToString()),
            new(ApiKeyClaims.CredentialId, "storefront:" + principal.AppId.ToString("N")),
        };
        if (principal.DeviceId is { } deviceId && principal.DeviceLocationId is { } deviceLocationId)
        {
            claims.Add(new Claim(StorefrontKeyAuthentication.DeviceClaim, deviceId.ToString("D")));
            claims.Add(new Claim(StorefrontKeyAuthentication.DeviceLocationClaim, deviceLocationId.ToString("D")));
        }

        if (principal.CustomerSessionId is { } sessionId && principal.CustomerId is { } customerId)
        {
            claims.Add(new Claim(StorefrontKeyAuthentication.CustomerSessionClaim, sessionId.ToString("D")));
            claims.Add(new Claim(StorefrontKeyAuthentication.CustomerClaim, customerId.ToString("D")));
        }

        var identity = new ClaimsIdentity(claims, StorefrontKeyAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>L'application de la requête : seulement si la requête a été authentifiée par clé publique.</summary>
internal sealed class HttpStorefrontClient(IHttpContextAccessor accessor, Storefront.Application.Checkout.ClientAddressKeys addressKeys) : IStorefrontClient
{
    /// <summary>La même adresse que les limites de débit (RemoteIpAddress).</summary>
    public string? ClientKey => addressKeys.For(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString());

    public SalesAppId? AppId => Read(StorefrontKeyAuthentication.AppClaim) is { } id ? SalesAppId.From(id) : null;

    public KioskDeviceId? DeviceId => Read(StorefrontKeyAuthentication.DeviceClaim) is { } id ? KioskDeviceId.From(id) : null;

    public LocationId? DeviceLocationId => Read(StorefrontKeyAuthentication.DeviceLocationClaim) is { } id ? LocationId.From(id) : null;

    public CustomerSessionId? CustomerSessionId =>
        Read(StorefrontKeyAuthentication.CustomerSessionClaim) is { } id ? Storefront.Domain.CustomerSessionId.From(id) : null;

    public CustomerId? CustomerId => Read(StorefrontKeyAuthentication.CustomerClaim) is { } id ? SaasRestaurant.BuildingBlocks.Domain.CustomerId.From(id) : null;

    private Guid? Read(string claim) =>
        accessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == StorefrontKeyAuthentication.Scheme) == true
        && Guid.TryParse(accessor.HttpContext.User.FindFirst(claim)?.Value, out var id) && id != Guid.Empty
            ? id
            : null;
}

/// <summary>Contrôle anti-robot avant le traitement d'une route sensible (commande, réservation, code de connexion).</summary>
public static class HumanCheckFilter
{
    public static RouteHandlerBuilder RequireHumanCheck(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var token = http.Request.Headers.TryGetValue(StorefrontKeyAuthentication.HumanCheckHeaderName, out var values) && values.Count == 1 ? values[0] : null;
            await http.RequestServices.GetRequiredService<ICommandHandler<VerifyHumanCheckCommand>>()
                .HandleAsync(new VerifyHumanCheckCommand(token, http.Connection.RemoteIpAddress?.ToString()), http.RequestAborted);
            return await next(context);
        });
}

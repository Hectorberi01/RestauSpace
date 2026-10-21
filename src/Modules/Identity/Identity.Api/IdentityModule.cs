using System.Security.Claims;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;
using Identity.Infrastructure;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SaasRestaurant.BuildingBlocks.Api;

namespace Identity.Api;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString,
        Action<AccessTokenOptions> configureAccessTokens,
        Action<AuthRateLimitOptions>? configureAuthRateLimit = null,
        Action<TwoFactorOptions>? configureTwoFactor = null)
    {
        var authRateLimit = new AuthRateLimitOptions();
        configureAuthRateLimit?.Invoke(authRateLimit);

        services.AddIdentityInfrastructure(connectionString, configureAccessTokens, configureTwoFactor);
        services.AddAuthRateLimiting(authRateLimit);
        services.AddHandlersFrom(typeof(LoginHandler).Assembly);
        return services;
    }

    /// <summary>
    /// Accepte les access tokens émis par ce module, validés avec les options mêmes qui servent à les signer : émission
    /// et validation ne peuvent pas diverger.
    /// </summary>
    public static AuthenticationBuilder AddIdentityAccessTokens(this AuthenticationBuilder authentication)
    {
        authentication.AddJwtBearer();
        authentication.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AccessTokenOptions>((bearer, accessTokens) =>
            {
                // Conserver les noms de claims du token ("sub", "sid", "email").
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = accessTokens.Issuer,
                    ValidAudience = accessTokens.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(accessTokens.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = IdentityClaims.UserId,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = EnsureSessionIsActive };
            });
        return authentication;
    }

    /// <summary>
    /// Une signature valide ne suffit pas : le token est refusé dès que sa session est déconnectée ou révoquée, ou
    /// son compte verrouillé ou désactivé. Une requête indexée par requête authentifiée.
    /// </summary>
    private static async Task EnsureSessionIsActive(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var principal = context.Principal;
        try
        {
            if (principal is null
                || !Guid.TryParse(principal.FindFirstValue(IdentityClaims.UserId), out var userId)
                || principal.GetSessionId() is not { } sessionId
                || !await services.GetRequiredService<ISessionStatus>().IsActiveAsync(
                    UserId.From(userId),
                    SessionId.From(sessionId),
                    services.GetRequiredService<IClock>().UtcNow,
                    context.HttpContext.RequestAborted))
            {
                context.Fail("The session of this access token is no longer active.");
            }
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            context.NoResult();
        }
    }

    public static async Task MigrateIdentityDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

using System.Security.Cryptography;
using Audit.Api;
using Audit.Application;
using Billing.Api;
using Billing.Application;
using Billing.Application.Abstractions;
using Catalog.Api;
using Catalog.Application;
using Catalog.Application.Abstractions;
using Customers.Api;
using Customers.Application;
using Customers.Application.Abstractions;
using Files.Api;
using Files.Application;
using Files.Application.Abstractions;
using Files.Infrastructure;
using Floor.Api;
using Floor.Application;
using Floor.Application.Abstractions;
using Identity.Api;
using Identity.Application.Abstractions;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure;
using Integrations.Api;
using Integrations.Application;
using Integrations.Application.Abstractions;
using Inventory.Api;
using Inventory.Application;
using Inventory.Application.Abstractions;
using Kitchen.Api;
using Kitchen.Application;
using Kitchen.Application.Abstractions;
using Locations.Api;
using Locations.Application.Abstractions;
using Locations.Application;
using Loyalty.Api;
using Loyalty.Application;
using Loyalty.Application.Abstractions;
using Notifications.Api;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Orders.Api;
using Orders.Application;
using Orders.Application.Abstractions;
using Payments.Api;
using Payments.Application;
using Payments.Application.Abstractions;
using Promotions.Api;
using Promotions.Application;
using Promotions.Application.Abstractions;
using Reporting.Api;
using Reporting.Application;
using Reporting.Application.Abstractions;
using Reservations.Api;
using Reservations.Application;
using Reservations.Application.Abstractions;
using Reviews.Api;
using Reviews.Application;
using Reviews.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Api;
using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Staff.Api;
using Printing.Api;
using Storefront.Api;
using Staff.Application;
using Staff.Application.Abstractions;
using Tenants.Api;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddIdentityAndAccess(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Identity : comptes, sessions et access tokens qui protègent toutes les routes ci-dessous.
        var accessTokens = builder.Configuration.GetSection("Identity:AccessTokens");
        var signingKey = accessTokens["SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException("'Identity:AccessTokens:SigningKey' is not configured.");
            }

            // Commodité de développement : une clé valable pour cette exécution seulement. Redémarrer l'hôte invalide
            // les access tokens de tout le monde.
            signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        // Sessions : durée maximale d'une famille (jours) et durée d'une session de caisse (heures).
        var sessions = builder.Configuration.GetSection("Identity:Sessions");
        var defaults = new Identity.Application.IdentitySettings();
        builder.Services.AddSingleton(defaults with
        {
            SessionFamilyLifetime = Identity.Domain.Users.SessionLifetime.Create(TimeSpan.FromDays(
                sessions.GetValue("FamilyMaxDays", defaults.SessionFamilyLifetime.Duration.TotalDays))),
            TillSessionLifetime = Identity.Domain.Users.SessionLifetime.Create(TimeSpan.FromHours(
                sessions.GetValue("TillSessionHours", defaults.TillSessionLifetime.TotalHours))).Duration,
        });

        var authRateLimit = builder.Configuration.GetSection("Identity:RateLimiting:Auth");
        builder.Services.AddIdentityModule(
            builder.ConnectionStringFor("Identity", connectionString),
            options =>
            {
                options.Issuer = accessTokens["Issuer"] ?? string.Empty;
                options.Audience = accessTokens["Audience"] ?? string.Empty;
                options.SigningKey = signingKey;
                if (accessTokens.GetValue<int?>("LifetimeMinutes") is { } minutes)
                {
                    options.Lifetime = TimeSpan.FromMinutes(minutes);
                }
            },
            limit =>
            {
                limit.PermitLimit = authRateLimit.GetValue("PermitLimit", limit.PermitLimit);
                limit.Window = TimeSpan.FromSeconds(authRateLimit.GetValue("WindowSeconds", limit.Window.TotalSeconds));
            },
            // Clé de chiffrement des secrets des applications d'authentification (Base64, 32 octets) : coffre de secrets.
            twoFactor => twoFactor.SecretKey = builder.Configuration["Identity:TwoFactor:SecretKey"]);
        var emails = builder.Configuration.GetSection("Identity:Emails");
        void ConfigureEmails(IdentityEmailOptions options)
        {
            options.ProductName = emails["ProductName"] ?? options.ProductName;
            options.VerifyEmailUrl = emails["Links:VerifyEmail"] ?? string.Empty;
            options.ResetPasswordUrl = emails["Links:ResetPassword"] ?? string.Empty;
            options.AcceptInvitationUrl = emails["Links:AcceptInvitation"] ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(emails["Smtp:Host"]))
        {
            builder.Services.AddIdentitySmtpEmails(ConfigureEmails, smtp =>
            {
                smtp.Host = emails["Smtp:Host"]!;
                smtp.Port = emails.GetValue("Smtp:Port", smtp.Port);
                smtp.Security = emails.GetValue("Smtp:Security", smtp.Security);
                smtp.Username = emails["Smtp:Username"];
                smtp.Password = emails["Smtp:Password"];
                smtp.FromAddress = emails["Smtp:FromAddress"] ?? string.Empty;
                smtp.FromName = emails["Smtp:FromName"];
            });
        }
        else if (emails.GetValue<bool>("WriteToLog"))
        {
            if (!builder.Environment.IsDevelopment())
            {
                // Le journal contiendrait tous les liens de réinitialisation et d'invitation.
                throw new InvalidOperationException("'Identity:Emails:WriteToLog' is only allowed in Development.");
            }

            builder.Services.AddIdentityLogEmails(ConfigureEmails);
        }

        if (builder.Configuration.GetValue<bool>("Identity:Passwords:RejectCompromised"))
        {
            builder.Services.AddIdentityCompromisedPasswordCheck();
        }

        // Deux façons de s'authentifier : l'access token d'un utilisateur (Bearer), ou une clé d'API d'intégration (en-tête
        // X-Api-Key, module Integrations). Le schéma par défaut choisit selon l'en-tête ; une clé n'atteint que les routes
        // protégées par une permission qu'elle porte en scope (UseApiKeyRestrictions, PermissionAuthorizationHandler).
        // Troisième schéma, jamais par défaut : la clé publique d'une application de vente (X-Storefront-Key), demandée
        // seulement par les routes /v1/storefront. Quatrième, jamais par défaut non plus : le jeton d'une imprimante, dans
        // l'URL des routes /v1/printing/cloudprnt et /v1/printing/relay. Cinquième : la clé d'une caisse partagée
        // (X-Till-Key), demandée seulement par les routes /v1/till.
        builder.Services.AddAuthentication(ApiKeyAuthentication.SelectorScheme)
            .AddIdentityAccessTokens()
            .AddIntegrationsApiKeys()
            .AddStorefrontKeys()
            .AddPrinterTokens()
            .AddTillKeys();
        var platformAdmins = new PlatformAdmins(
            builder.Configuration.GetSection("Authorization:PlatformAdminEmails").Get<string[]>() ?? [],
            builder.Configuration.GetValue("Authorization:PlatformAdminRequireTwoFactor", true));
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.PlatformAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => platformAdmins.Contains(context.User)))
            .AddTenantMemberPolicy();
        return builder;
    }
}

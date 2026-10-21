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
using Staff.Application;
using Staff.Application.Abstractions;
using Tenants.Api;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddIntegrationsComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Integrations : webhooks sortants signés (convention Standard Webhooks, HMAC-SHA256) et clés d'API des
        // restaurants. Les événements publics d'Orders, Payments, Reservations, Kitchen et Inventory deviennent des livraisons
        // (WebhookFeed, par l'outbox, une fois par événement), envoyées par le job du module avec nouvelles tentatives, sans
        // jamais joindre une adresse non publique (anti-SSRF à la connexion). Les secrets de signature sont chiffrés avec
        // Integrations:SecretsKey (32 octets en base64, OBLIGATOIRE, à garder secrète). Une clé d'API ne reçoit que des
        // scopes du catalogue ci-dessous : les permissions des restaurants, SAUF la gestion du personnel, des intégrations
        // et de l'abonnement (une clé ne peut ni se donner des droits, ni créer d'autres clés).
        var integrations = builder.Configuration.GetSection("Integrations");
        var integrationsKey = integrations["SecretsKey"];
        if (string.IsNullOrWhiteSpace(integrationsKey))
        {
            throw new InvalidOperationException("'Integrations:SecretsKey' is not configured (32 bytes, base64).");
        }

        string[] apiKeyClosedPrefixes = ["staff.", "integrations.", "billing.", "audit.", "tenant.", "storefront."];
        builder.Services.AddIntegrationsModule(
            builder.ConnectionStringFor("Integrations", connectionString),
            options =>
            {
                options.SecretsKey = Convert.FromBase64String(integrationsKey);
                options.RunWorkers = integrations.GetValue("RunWorkers", true);
            },
            integrationsSettings =>
            {
                foreach (var permission in TenantPermissions.Distinct(StringComparer.Ordinal)
                    .Where(permission => !apiKeyClosedPrefixes.Any(prefix => permission.StartsWith(prefix, StringComparison.Ordinal))))
                {
                    integrationsSettings.ApiScopes.Add(permission);
                }

                integrationsSettings.AutoDisableAfterDeadLetters = integrations.GetValue("AutoDisableAfterDeadLetters", integrationsSettings.AutoDisableAfterDeadLetters);
                integrationsSettings.SecretGracePeriod = integrations.GetValue("SecretGracePeriod", integrationsSettings.SecretGracePeriod);
                integrationsSettings.DormantKeyAfter = integrations.GetValue("DormantKeyAfter", integrationsSettings.DormantKeyAfter);
            });
        // Débit par clé d'API (en mémoire de l'instance) : Integrations:ApiKeyRateLimit:PermitLimit et :WindowSeconds.
        var apiKeyRateLimit = integrations.GetSection("ApiKeyRateLimit");
        builder.Services.AddIntegrationsApiKeyRateLimit(limit =>
        {
            limit.PermitLimit = apiKeyRateLimit.GetValue("PermitLimit", limit.PermitLimit);
            limit.Window = TimeSpan.FromSeconds(apiKeyRateLimit.GetValue("WindowSeconds", limit.Window.TotalSeconds));
        });
        builder.Services.AddScoped<IIntegrationTenants, IntegrationTenantsFromTenants>();
        builder.Services.AddScoped<IOutboxConsumer, WebhookFeed>();
        // Un endpoint désactivé par le système est annoncé aux gestionnaires des intégrations (Notifications, in-app).
        builder.Services.AddScoped<IOutboxConsumer, WebhookHealthFlow>();
        return builder;
    }
}

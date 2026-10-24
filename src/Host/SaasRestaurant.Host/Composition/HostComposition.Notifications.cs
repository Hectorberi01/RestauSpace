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
    public static WebApplicationBuilder AddNotificationsComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Notifications : la plateforme définit les prestataires qu'elle sait utiliser (routes /admin/notification-providers),
        // chaque restaurant choisit les siens et saisit les identifiants de SON compte (chiffrés avec Notifications:CredentialsKey,
        // 32 octets en base64, OBLIGATOIRE et à garder secrète). Les codes à usage unique ne sont gardés que sous forme
        // d'empreinte HMAC (Notifications:OtpKey, 32 octets au moins, OBLIGATOIRE). Le prestataire simulé « FAKE » n'existe
        // qu'avec Notifications:EnableFakeProvider, en développement seulement. Les événements d'Orders, Reservations et
        // Payments deviennent des notifications (NotificationTriggersFlow, par l'outbox, une fois par événement).
        // Notifications:UnsubscribePageUrl : la page de désinscription du site public ; sans elle, aucun e-mail marketing ne part.
        // Notifications:AnonymousSmsPerTenantPerHour : SMS + WhatsApp déclenchés par les visiteurs du site, par restaurant et
        // par heure (défaut 100, 0 = sans plafond) ; ces envois ne partent que vers un numéro du pays du restaurant.
        var notifications = builder.Configuration.GetSection("Notifications");
        var notificationsKey = notifications["CredentialsKey"];
        var otpKey = notifications["OtpKey"];
        if (string.IsNullOrWhiteSpace(notificationsKey) || string.IsNullOrWhiteSpace(otpKey))
        {
            throw new InvalidOperationException("'Notifications:CredentialsKey' and 'Notifications:OtpKey' are not configured (32 bytes, base64).");
        }

        var enableFakeNotifications = notifications.GetValue<bool>("EnableFakeProvider");
        if (enableFakeNotifications && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("The fake notification provider is only allowed in Development.");
        }

        builder.Services.AddNotificationsModule(
            builder.ConnectionStringFor("Notifications", connectionString),
            options =>
            {
                options.CredentialsKey = Convert.FromBase64String(notificationsKey);
                options.OtpKey = Convert.FromBase64String(otpKey);
                options.EnableFakeProvider = enableFakeNotifications;
            },
            settings =>
            {
                settings.DefaultLanguage = notifications["DefaultLanguage"] ?? settings.DefaultLanguage;
                settings.UnsubscribePageUrl = notifications["UnsubscribePageUrl"];
                settings.AnonymousSmsPerTenantPerHour = notifications.GetValue("AnonymousSmsPerTenantPerHour", settings.AnonymousSmsPerTenantPerHour);
            });
        builder.Services.AddScoped<INotificationTenants, NotificationTenantsFromTenants>();
        builder.Services.AddScoped<INotificationLocations, NotificationLocationsFromLocations>();
        builder.Services.AddScoped<INotificationContacts, NotificationContactsFromCustomers>();
        builder.Services.AddScoped<INotificationConsents, NotificationConsentsFromCustomers>();
        builder.Services.AddScoped<ICampaignAudience, CampaignAudienceFromCustomers>();
        builder.Services.AddScoped<IOutboxConsumer, NotificationTriggersFlow>();
        return builder;
    }
}

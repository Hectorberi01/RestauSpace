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
    public static WebApplicationBuilder AddReportingComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Reporting : compteurs du jour par restaurant (ventes, paiements, réservations, cuisine, stock, promotions,
        // fidélité), alimentés par les événements des autres modules (ReportingFeed, par l'outbox, une fois par message) ; vue
        // de la plateforme (restaurants, abonnements, MRR) recopiée depuis Tenants et Billing (ReportingPlatformFeed) ; rapports
        // enregistrés, exports CSV et envois planifiés (job du module). Un export terminé est annoncé par Notifications
        // (ReportingDeliveryFlow) avec un lien vers la console (Reporting:PublicBaseUrl/rapports/planifies). Les fichiers restent en base jusqu'au module Files.
        var reporting = builder.Configuration.GetSection("Reporting");
        builder.Services.AddReportingModule(
            builder.ConnectionStringFor("Reporting", connectionString),
            options => options.RunWorkers = reporting.GetValue("RunWorkers", true),
            reportingSettings => reportingSettings.ExportRetention = reporting.GetValue("ExportRetention", reportingSettings.ExportRetention));
        builder.Services.AddScoped<IReportingTenants, ReportingTenantsFromTenants>();
        builder.Services.AddScoped<IReportingLocationNames, ReportingLocationNamesFromLocations>();
        builder.Services.AddScoped<IOutboxConsumer, ReportingFeed>();
        builder.Services.AddScoped<IOutboxConsumer, ReportingPlatformFeed>();
        builder.Services.AddScoped<IOutboxConsumer, ReportingDeliveryFlow>();
        return builder;
    }
}

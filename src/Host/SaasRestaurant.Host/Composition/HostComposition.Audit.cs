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
    public static WebApplicationBuilder AddAuditComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Audit : journal en AJOUT SEUL des actions sensibles (comptes et sessions, rôles et personnel, restaurant,
        // remboursements et prestataires de paiement, abonnement, corrections de stock, consentements, exports, clés d'API et
        // webhooks). Alimenté par l'outbox des autres modules (AuditFeed : une règle et une liste blanche de champs par
        // événement, une seule écriture par événement). Mise à jour et suppression refusées EN BASE (triggers) ; seule la purge
        // de rétention (Audit:RetentionDays, 1095 par défaut, 0 = illimitée) supprime. Consultation : /audit (audit.view) et
        // /admin/audit (plateforme).
        var audit = builder.Configuration.GetSection("Audit");
        builder.Services.AddAuditModule(builder.ConnectionStringFor("Audit", connectionString), options =>
        {
            options.RunWorkers = audit.GetValue("RunWorkers", true);
            options.Retention = TimeSpan.FromDays(audit.GetValue("RetentionDays", options.Retention.TotalDays));
        });
        builder.Services.AddScoped<IOutboxConsumer, AuditFeed>();
        return builder;
    }
}

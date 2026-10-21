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
    public static WebApplicationBuilder AddLoyaltyComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Loyalty : comptes de points (registre en insertion seule), récompenses et barème. Une commande terminée rapporte
        // des points, une commande annulée les reprend (et rend une récompense utilisée), un client anonymisé voit son compte
        // clos, et la remise d'une récompense est posée sur la commande (LoyaltyFlow, par l'outbox). Les points d'un compte
        // inactif expirent selon le barème (job du module).
        builder.Services.AddLoyaltyModule(builder.ConnectionStringFor("Loyalty", connectionString), options =>
            options.RunWorkers = builder.Configuration.GetValue("Loyalty:RunWorkers", true));
        builder.Services.AddScoped<ILoyaltyTenants, LoyaltyTenantsFromTenants>();
        builder.Services.AddScoped<ILoyaltyCustomers, LoyaltyCustomersFromCustomers>();
        builder.Services.AddScoped<ILoyaltyOrders, LoyaltyOrdersFromOrders>();
        builder.Services.AddScoped<IOutboxConsumer, LoyaltyFlow>();
        return builder;
    }
}

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
    public static WebApplicationBuilder AddReviewsComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Reviews : avis des clients (note 1-5, commentaire facultatif, client et commande facultatifs, un avis par
        // commande) et questionnaires du restaurant. Lecture reviews.view (les commentaires sont la parole des clients),
        // enregistrement reviews.submit (tablette, application par clé d'API), traitement et questionnaires reviews.manage.
        // L'anonymisation d'un client retire l'auteur et le commentaire de ses avis (ReviewsAnonymizationFlow) ; une note
        // inférieure ou égale à Reviews:LowRatingAlertThreshold (2 par défaut, 0 = jamais) est signalée en in-app aux membres
        // qui traitent les avis de l'établissement (ReviewAlertFlow, type staff.new_review), sans commentaire ni auteur.
        builder.Services.AddReviewsModule(builder.ConnectionStringFor("Reviews", connectionString));
        builder.Services.AddScoped<IReviewTenants, ReviewTenantsFromTenants>();
        builder.Services.AddScoped<IReviewLocations, ReviewLocationsFromLocations>();
        builder.Services.AddScoped<IReviewOrders, ReviewOrdersFromOrders>();
        builder.Services.AddScoped<IReviewCustomers, ReviewCustomersFromCustomers>();
        builder.Services.AddScoped<IOutboxConsumer, ReviewsAnonymizationFlow>();
        builder.Services.AddSingleton(new ReviewAlertSettings(
            Math.Clamp(builder.Configuration.GetValue("Reviews:LowRatingAlertThreshold", 2), 0, Reviews.Domain.Rating.Max)));
        builder.Services.AddScoped<IOutboxConsumer, ReviewAlertFlow>();
        return builder;
    }
}

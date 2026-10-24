using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Api;
using Storefront.Application.Abstractions;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddStorefrontComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Storefront : la vente en ligne du restaurant (sites, applications en marque blanche, bornes). Le restaurant
        // crée ses applications de vente et leurs clés publiques (pk_live_…) dans la console ; chaque requête publique
        // (/v1/storefront) est authentifiée par cette clé, qui donne le tenant. Les canaux sont des options de
        // l'abonnement (droits storefront.* de Billing). Document OpenAPI publié : /openapi/storefront.json.
        // Storefront:TrackingKey (32 octets en Base64, OBLIGATOIRE et secrète) signe les jetons de suivi remis aux clients.
        var storefront = builder.Configuration.GetSection("Storefront");
        builder.Services.AddStorefrontModule(builder.ConnectionStringFor("Storefront", connectionString), options =>
        {
            options.TrackingKey = storefront["TrackingKey"] ?? string.Empty;
            options.RunWorkers = storefront.GetValue("RunWorkers", true);
            storefront.GetSection("Turnstile").Bind(options.Turnstile);
            // Commandes impayées : MaxPendingPayments (3) par application, IP et client ; PaymentHoldMinutes (10) au plus pour payer.
            storefront.GetSection("CheckoutLimits").Bind(options.CheckoutLimits);
        });
        builder.Services.AddScoped<IStorefrontTenants, StorefrontTenantsFromTenants>();
        builder.Services.AddScoped<IStorefrontLocations, StorefrontLocationsFromLocations>();
        builder.Services.AddScoped<IStorefrontEntitlements, StorefrontEntitlementsFromBilling>();
        builder.Services.AddScoped<IStorefrontCatalog, StorefrontCatalogFromCatalog>();
        builder.Services.AddScoped<IStorefrontOrders, StorefrontOrdersFromOrders>();
        builder.Services.AddScoped<IStorefrontTables, StorefrontTablesFromFloor>();
        builder.Services.AddScoped<IStorefrontSurveys, StorefrontSurveysFromReviews>();
        builder.Services.AddScoped<IStorefrontReviews, StorefrontReviewsFromReviews>();
        builder.Services.AddScoped<IStorefrontReservations, StorefrontReservationsFromReservations>();
        builder.Services.AddScoped<IStorefrontCustomers, StorefrontCustomersFromCustomers>();
        builder.Services.AddScoped<IStorefrontPayments, StorefrontPaymentsFromPayments>();
        builder.Services.AddScoped<IStorefrontDelivery, StorefrontDeliveryFromDelivery>();
        builder.Services.AddScoped<IStorefrontCoupons, StorefrontCouponsFromPromotions>();
        builder.Services.AddScoped<IStorefrontOtp, StorefrontOtpFromNotifications>();
        builder.Services.AddScoped<IStorefrontLoyalty, StorefrontLoyaltyFromLoyalty>();
        // Reçus : lien signé vers la page /recu du site du restaurant, et envoi par WhatsApp/SMS (type order.receipt).
        builder.Services.AddScoped<IStorefrontReceipts, StorefrontReceiptsFromOrders>();
        builder.Services.AddScoped<IStorefrontReceiptMessages, StorefrontReceiptMessagesFromNotifications>();
        builder.Services.AddScoped<IOutboxConsumer, StorefrontPaymentFlow>();
        // Document public : seulement les routes /v1/storefront (groupe storefront), jamais les routes de la console ou de l'administration.
        builder.Services.AddOpenApi(StorefrontEndpoints.DocumentName, options =>
        {
            options.ShouldInclude = description => string.Equals(description.GroupName, StorefrontEndpoints.DocumentName, StringComparison.Ordinal);
            options.AddDocumentTransformer<StorefrontDocumentTransformer>();
        });
        return builder;
    }
}

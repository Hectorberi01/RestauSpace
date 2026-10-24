using System.Security.Cryptography;
using Audit.Api;
using Audit.Application;
using Backups.Api;
using Billing.Api;
using Billing.Application;
using Billing.Application.Abstractions;
using Catalog.Api;
using Catalog.Application;
using Catalog.Application.Abstractions;
using Customers.Api;
using Delivery.Api;
using Delivery.Application;
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
using Scalar.AspNetCore;
using Staff.Api;
using Printing.Api;
using Fiscal.Api;
using Storefront.Api;
using Staff.Application;
using Staff.Application.Abstractions;
using Tenants.Api;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplication UsePipelineAndRoutes(this WebApplication app)
    {
        if (app.Configuration.GetValue("ReverseProxy:Enabled", false))
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();
        app.UseCors();
        app.UseAuthentication();
        app.UseApiKeyRestrictions();
        app.UseTillSessionRestrictions();
        app.UseRequestLogScope();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseIdempotencyKeys();

        app.MapHealthChecks("/health");
        // Le document de la vente en ligne (storefront) est toujours publié : les agences des restaurants s'en servent.
        var exposeAllDocuments = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Api:ExposeOpenApi");
        if (!exposeAllDocuments)
        {
            var published = "/openapi/" + StorefrontEndpoints.DocumentName + ".json";
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/openapi") && !context.Request.Path.Equals(published, StringComparison.OrdinalIgnoreCase),
                hidden => hidden.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }));
        }

        app.MapOpenApi();
        if (exposeAllDocuments)
        {
            app.MapScalarApiReference("/docs", options => options
                .WithTitle("RestauSpace API")
                .AddPreferredSecuritySchemes(ApiDocumentTransformer.BearerScheme));
        }

        var v1 = app.MapGroup("/v1");
        v1.MapOutboxAdministration().RequireAuthorization(Policies.PlatformAdmin);
        v1.MapIdentityEndpoints(Policies.PlatformAdmin);
        v1.MapTenantsEndpoints().RequireAuthorization(Policies.PlatformAdmin);
        v1.MapCurrentTenantEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var demoRequestRoutes = v1.MapDemoRequestEndpoints();
        // Formulaire du site vitrine : sans compte, limité en débit par adresse client, piège à robots dans le module.
        demoRequestRoutes.Public.AllowAnonymous();
        demoRequestRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        v1.MapLocationsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var staffRoutes = v1.MapStaffEndpoints();
        staffRoutes.Staff.RequireAuthorization(Tenancy.TenantMemberPolicy);
        staffRoutes.MyMemberships.RequireAuthorization();
        staffRoutes.Platform.RequireAuthorization(Policies.PlatformAdmin);
        // La caisse partagée a sa propre politique (clé de l'appareil), posée par le module.
        v1.MapCatalogEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        // Menu ouvert par le QR code d'une table : sans compte, en lecture seule, limité en débit par adresse client.
        v1.MapPublicMenuEndpoints().AllowAnonymous();
        v1.MapSignUpEndpoints();
        v1.MapFloorEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapReservationsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapOrdersEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapKitchenEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapInventoryEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapCustomersEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var paymentRoutes = v1.MapPaymentsEndpoints();
        paymentRoutes.Payments.RequireAuthorization(Tenancy.TenantMemberPolicy);
        // Les prestataires n'ont pas de jeton : l'authenticité d'un webhook vient de sa signature, vérifiée par le module.
        paymentRoutes.Webhooks.AllowAnonymous();
        paymentRoutes.Platform.RequireAuthorization(Policies.PlatformAdmin);
        var notificationRoutes = v1.MapNotificationsEndpoints();
        notificationRoutes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        notificationRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        // Les prestataires n'ont pas de jeton : l'authenticité d'un webhook vient de sa signature, vérifiée par le module.
        notificationRoutes.Webhooks.AllowAnonymous();
        // Lien de désinscription d'un e-mail marketing : sans compte, l'autorisation est la signature du jeton.
        notificationRoutes.Public.AllowAnonymous().RequireRateLimiting(PublicMenuEndpoints.RateLimitPolicy);
        v1.MapLoyaltyEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapPromotionsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var billingRoutes = v1.MapBillingEndpoints();
        billingRoutes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        billingRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        var reportingRoutes = v1.MapReportingEndpoints();
        reportingRoutes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        reportingRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        v1.MapFilesEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        // Photos du catalogue en ligne : sans compte, l'autorisation est la signature du jeton de l'URL.
        v1.MapPublicMediaEndpoints().AllowAnonymous().RequireRateLimiting(PublicMenuEndpoints.MediaRateLimitPolicy);
        if (app.Services.GetRequiredService<FilesOptions>().Storage == FileStorageProvider.Local)
        {
            // Stockage local de développement : l'autorisation est la signature du jeton de l'URL, comme pour R2.
            v1.MapFilesLocalStorageEndpoints().AllowAnonymous();
        }
        v1.MapIntegrationsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var auditRoutes = v1.MapAuditEndpoints();
        auditRoutes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        auditRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        v1.MapReviewsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapDeliveryEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var storefrontRoutes = v1.MapStorefrontEndpoints();
        storefrontRoutes.Console.RequireAuthorization(Tenancy.TenantMemberPolicy);
        storefrontRoutes.Admin.RequireAuthorization(Policies.PlatformAdmin);
        // Les imprimantes ont leur propre politique (jeton dans l'URL), posée par le module.
        var printingRoutes = v1.MapPrintingEndpoints();
        printingRoutes.Console.RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapFiscalEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        v1.MapBackupsEndpoints().RequireAuthorization(Policies.PlatformAdmin);
        return app;
    }
}

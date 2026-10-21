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
    public static WebApplicationBuilder AddPaymentsComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Payments : la plateforme définit les prestataires qu'elle supporte (catalogue, routes /platform/payment-providers),
        // chaque restaurant choisit ceux qu'il accepte et saisit les identifiants de SON compte marchand (chiffrés avec
        // Payments:CredentialsKey, 32 octets en base64, OBLIGATOIRE et à garder secrète). L'intégration simulée « sandbox »
        // n'existe qu'avec Payments:EnableSandbox : en développement, ou en recette avec Payments:AllowSandboxOutsideDevelopment.
        // JAMAIS en production : un restaurant pourrait « encaisser » sans argent réel. Prestataires réels : FedaPay, KKiaPay,
        // CinetPay, MTN MoMo, Stripe et PayPal ; Payments:PublicApiBaseUrl (adresse publique de l'API) leur donne l'adresse de
        // notification, Payments:CheckoutReturnUrl la page de retour du client.
        var payments = builder.Configuration.GetSection("Payments");
        var credentialsKey = payments["CredentialsKey"];
        if (string.IsNullOrWhiteSpace(credentialsKey))
        {
            throw new InvalidOperationException("'Payments:CredentialsKey' is not configured (32 bytes, base64).");
        }

        var enableSandbox = payments.GetValue<bool>("EnableSandbox");
        if (enableSandbox && !builder.Environment.IsDevelopment() && !payments.GetValue<bool>("AllowSandboxOutsideDevelopment"))
        {
            throw new InvalidOperationException("The 'sandbox' payment integration is only allowed in Development.");
        }

        builder.Services.AddPaymentsModule(builder.ConnectionStringFor("Payments", connectionString), options =>
        {
            options.CredentialsKey = Convert.FromBase64String(credentialsKey);
            options.EnableSandbox = enableSandbox;
            options.SandboxCheckoutBaseUrl = payments["SandboxCheckoutBaseUrl"] is { Length: > 0 } checkout ? new Uri(checkout) : null;
            options.PublicApiBaseUrl = payments["PublicApiBaseUrl"] is { Length: > 0 } api ? new Uri(api) : null;
            options.CheckoutReturnUrl = payments["CheckoutReturnUrl"] is { Length: > 0 } back ? new Uri(back) : null;
        });
        builder.Services.AddScoped<IPaymentTenants, PaymentTenantsFromTenants>();
        builder.Services.AddScoped<IPaymentTenantCurrency, PaymentTenantCurrencyFromTenants>();
        builder.Services.AddScoped<IPaymentOrders, PaymentOrdersFromOrders>();
        builder.Services.AddScoped<IPaymentLocations, PaymentLocationsFromLocations>();
        builder.Services.AddScoped<IPaymentStoredValue, PaymentStoredValueFromLoyalty>();
        return builder;
    }
}

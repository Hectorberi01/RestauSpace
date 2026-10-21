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
    public static WebApplicationBuilder AddBillingComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Billing : catalogue des plans (plateforme), abonnement de chaque restaurant (essai, paiement enregistré par la
        // plateforme, impayé, suspension, résiliation) et compteurs de consommation. Le job d'échéance traite les fins d'essai,
        // les impayés et les résiliations programmées ; chaque changement d'abonnement est répercuté sur le statut du
        // restaurant (BillingFlow, par l'outbox). Les quotas mensuels de Notifications sont ceux du plan : un restaurant sans
        // abonnement vivant n'a droit à aucune notification soumise à quota. Enregistré APRÈS Notifications, dont le quota par
        // défaut est en TryAdd.
        var billing = builder.Configuration.GetSection("Billing");
        builder.Services.AddBillingModule(
            builder.ConnectionStringFor("Billing", connectionString),
            options =>
            {
                options.RunWorkers = billing.GetValue("RunWorkers", true);
                billing.GetSection("InvoiceIssuer").Bind(options.InvoiceIssuer);
            },
            settings =>
            {
                settings.TrialLength = billing.GetValue("TrialLength", settings.TrialLength);
                settings.PastDueGracePeriod = billing.GetValue("PastDueGracePeriod", settings.PastDueGracePeriod);
            });
        builder.Services.AddScoped<IBillingTenants, BillingTenantsFromTenants>();
        var online = billing.GetSection("OnlinePayment");
        builder.Services.AddSingleton(new BillingOnlinePayment(
            string.IsNullOrWhiteSpace(online["Provider"]) ? null : online["Provider"]!.Trim().ToLowerInvariant(),
            Uri.TryCreate(online["ReturnUrl"], UriKind.Absolute, out var back) ? back : null));
        builder.Services.AddScoped<ISubscriptionCollector, SubscriptionCollectorFromPayments>();
        builder.Services.AddScoped<INotificationEntitlementChecker, NotificationEntitlementsFromBilling>();
        builder.Services.AddScoped<PlanLimitsFromBilling>();
        builder.Services.AddScoped<SignUpFlow>();
        builder.Services.AddScoped<IOrderPlanLimits>(provider => provider.GetRequiredService<PlanLimitsFromBilling>());
        builder.Services.AddScoped<ILocationPlanLimits>(provider => provider.GetRequiredService<PlanLimitsFromBilling>());
        builder.Services.AddScoped<IStaffPlanLimits>(provider => provider.GetRequiredService<PlanLimitsFromBilling>());
        builder.Services.AddScoped<IOutboxConsumer, BillingFlow>();
        builder.Services.AddScoped<IOutboxConsumer, SubscriptionNoticesFlow>();
        return builder;
    }
}

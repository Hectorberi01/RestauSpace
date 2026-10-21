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
using Staff.Api;
using Staff.Application;
using Staff.Application.Abstractions;
using Tenants.Api;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static string[] TenantPermissions => [.. LocationsPermissions.All, .. CatalogPermissions.All, .. FloorPermissions.All, .. ReservationsPermissions.All, .. OrdersPermissions.All, .. KitchenPermissions.All, .. PaymentsPermissions.All, .. InventoryPermissions.All, .. CustomersPermissions.All, .. NotificationsPermissions.All, .. LoyaltyPermissions.All, .. PromotionsPermissions.All, .. BillingPermissions.All, .. ReportingPermissions.All, .. FilesPermissions.All, .. IntegrationsPermissions.All, .. AuditPermissions.All, .. ReviewsPermissions.All, .. DeliveryPermissions.All, .. Tenants.Application.TenantsPermissions.All, .. Storefront.Application.StorefrontPermissions.All, .. Printing.Application.PrintingPermissions.All, .. Fiscal.Application.FiscalPermissions.All];

    public static WebApplicationBuilder AddStaffComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Staff & RBAC : qui appartient à quel restaurant, avec quelles permissions. Les codes de permission de chaque
        // module sont listés ici : ce sont les seuls qu'un rôle peut recevoir.
        builder.Services.AddStaffModule(builder.ConnectionStringFor("Staff", connectionString), TenantPermissions);
        builder.Services.AddScoped<StaffPermissionChecker>();
        builder.Services.AddScoped<IPermissionChecker, CachedPermissionChecker>();
        builder.Services.AddScoped<IStaffTenants, StaffTenantsFromTenants>();
        builder.Services.AddScoped<ILocationDirectory, LocationDirectoryFromLocations>();
        builder.Services.AddScoped<IUserAccounts, UserAccountsFromIdentity>();
        builder.Services.AddScoped<IOperatorSessions, OperatorSessionsFromIdentity>();
        builder.Services.AddScoped<IOutboxConsumer, StaffInvitationFlow>();
        builder.Services.AddScoped<IScheduleLocations, ScheduleLocationsFromLocations>();
        builder.Services.AddScoped<IOutboxConsumer, ScheduleNoticesFlow>();
        builder.Services.AddPermissionAuthorization();
        // Sélectionner un restaurant (POST /me/tenant) interroge désormais Staff : un profil actif dans un tenant non fermé.
        builder.Services.AddIdentityTenantMemberships<TenantMembershipsFromStaff>();
        return builder;
    }
}

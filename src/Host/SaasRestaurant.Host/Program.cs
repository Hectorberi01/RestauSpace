using SaasRestaurant.Host;
using Identity.Application.Abstractions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SaasRestaurant");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'SaasRestaurant' is not configured.");
}

builder.AddPlatform(connectionString);
builder.AddObservability();
builder.AddIdentityAndAccess(connectionString);
builder.AddTenantsComposition(connectionString);
builder.AddLocationsComposition(connectionString);
builder.AddStaffComposition(connectionString);
builder.AddCatalogComposition(connectionString);
builder.AddFloorComposition(connectionString);
builder.AddReservationsComposition(connectionString);
builder.AddOrdersComposition(connectionString);
builder.AddKitchenComposition(connectionString);
builder.AddPaymentsComposition(connectionString);
builder.AddInventoryComposition(connectionString);
builder.AddCustomersComposition(connectionString);
builder.AddNotificationsComposition(connectionString);
builder.AddLoyaltyComposition(connectionString);
builder.AddPromotionsComposition(connectionString);
builder.AddBillingComposition(connectionString);
builder.AddReportingComposition(connectionString);
builder.AddFilesComposition(connectionString);
builder.AddIntegrationsComposition(connectionString);
builder.AddAuditComposition(connectionString);
builder.AddReviewsComposition(connectionString);
builder.AddDeliveryComposition(connectionString);
builder.AddStorefrontComposition(connectionString);
builder.AddPrintingComposition(connectionString);
builder.AddFiscalComposition(connectionString);

var app = builder.Build();

if (app.Services.GetService<IIdentityNotifier>() is null)
{
    throw new InvalidOperationException(
        "No email sender is configured for Identity. Set 'Identity:Emails:Smtp:Host', or 'Identity:Emails:WriteToLog' in development.");
}

// Étape de déploiement : appliquer les migrations puis s'arrêter, avant de démarrer l'API (une ou plusieurs instances).
if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await app.Services.MigrateAllDatabasesAsync();
    return;
}

app.UsePipelineAndRoutes();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateAllDatabasesAsync();
}

// Développement : administrateur et restaurant de test (DevSeed:Enabled).
await app.SeedDevelopmentAsync();

await app.RunAsync();

public partial class Program;

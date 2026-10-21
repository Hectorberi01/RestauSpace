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
    public static WebApplicationBuilder AddFilesComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Files : métadonnées en base, octets dans Cloudflare R2 (API S3, signature SigV4). Le navigateur envoie et
        // télécharge DIRECTEMENT avec R2 par des URLs signées et courtes ; le serveur vérifie taille et type réel avant de rendre
        // un fichier disponible. Identifiants R2 : Files:R2:AccountId, AccessKeyId, SecretAccessKey, Bucket — par variables
        // d'environnement ou coffre de secrets, jamais dans appsettings versionné. Files:Storage=Memory en développement
        // seulement ; Files:Storage=Local (disque de l'API, URLs signées servies par /v1/files/local) aussi, pour envoyer et
        // afficher des photos sans compte R2. Les exports de Reporting sont stockés par Files (remplace leur stockage en base).
        // Photos du catalogue en ligne : liens publics stables /v1/media/{jeton} signés par Files:PublicMedia:SigningKey
        // (Base64, 32 octets au moins, secret) sur Files:PublicMedia:BaseUrl (l'API ou le CDN placé devant elle).
        var files = builder.Configuration.GetSection("Files");
        var fileStorage = files.GetValue("Storage", FileStorageProvider.R2);
        if (fileStorage is (FileStorageProvider.Memory or FileStorageProvider.Local) && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException($"Files:Storage={fileStorage} is only allowed in Development.");
        }

        builder.Services.AddFilesModule(builder.ConnectionStringFor("Files", connectionString), options =>
        {
            options.Storage = fileStorage;
            options.RunWorkers = files.GetValue("RunWorkers", true);
            files.GetSection("R2").Bind(options.R2);
            files.GetSection("Local").Bind(options.Local);
            files.GetSection("PublicMedia").Bind(options.PublicMedia);
        });
        builder.Services.AddScoped<IFileTenants, FileTenantsFromTenants>();
        builder.Services.AddScoped<IFileOwnerChecker, FileOwnersFromModules>();
        builder.Services.AddScoped<Reporting.Application.Abstractions.IExportFileStore, ReportingExportFilesFromFiles>();
        return builder;
    }
}

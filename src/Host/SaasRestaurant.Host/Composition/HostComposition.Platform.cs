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
    public static string ConnectionStringFor(this WebApplicationBuilder builder, string module, string fallback) =>
        builder.Configuration.GetConnectionString($"SaasRestaurant.{module}") is { Length: > 0 } dedicated ? dedicated : fallback;

    public static WebApplicationBuilder AddPlatform(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddApiConventions();

        // Derrière un proxy (Traefik) : l'adresse du client et le schéma viennent des en-têtes X-Forwarded-*, acceptés
        // seulement des réseaux déclarés (par défaut les plages privées, celles des réseaux Docker). Sans cela, toutes
        // les requêtes sembleraient venir du proxy et les limites de débit par adresse deviendraient globales.
        var proxy = builder.Configuration.GetSection("ReverseProxy");
        if (proxy.GetValue("Enabled", false))
        {
            var networks = proxy.GetSection("KnownNetworks").Get<string[]>() is { Length: > 0 } declared
                ? declared
                : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
            builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                    | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = proxy.GetValue("ForwardLimit", 1);
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var network in networks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });
        }

        builder.Services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.GroupName is null;
            options.AddDocumentTransformer<ApiDocumentTransformer>();
        });
        // Le tenant d'une requête est le claim "tid" de son access token (voir POST /me/tenant), jamais une valeur de la requête.
        builder.Services.AddRequestTenantContext();
        builder.Services.AddHealthChecks();
        builder.Services.AddOutboxMonitoring(TimeSpan.FromMinutes(builder.Configuration.GetValue("Outbox:MaxPendingMinutes", 5)));
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddPostgresIdempotencyStore(builder.ConnectionStringFor("Idempotency", connectionString));
        builder.Services.AddMemoryCache();
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("Location", "Retry-After", "Idempotency-Replayed", "Content-Disposition")
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            }
        }));
        var caching = builder.Configuration.GetSection("Caching");
        builder.Services.AddSingleton(new CacheSettings(
            TimeSpan.FromSeconds(caching.GetValue("TenantSeconds", 30)),
            TimeSpan.FromSeconds(caching.GetValue("PermissionSeconds", 30))));
        builder.Services.AddScoped<CachedTenants>();
        builder.Services.AddScoped<IOutboxConsumer, CacheInvalidationFlow>();
        return builder;
    }
}

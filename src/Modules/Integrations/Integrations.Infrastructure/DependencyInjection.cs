using Integrations.Application;
using Integrations.Application.Abstractions;
using Integrations.Infrastructure.Persistence;
using Integrations.Infrastructure.Webhooks;
using Integrations.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Integrations.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propriété du module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "integrations";

    /// <summary>
    /// Enregistre la persistance, le coffre des secrets, l'envoi protégé et le job du module. L'hôte fournit
    /// <see cref="ITenantContext"/> et <c>IIntegrationTenants</c>. <see cref="IntegrationsOptions.SecretsKey"/> est
    /// obligatoire (32 octets).
    /// </summary>
    public static IServiceCollection AddIntegrationsInfrastructure(
        this IServiceCollection services, string connectionString, Action<IntegrationsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var integrationsOptions = new IntegrationsOptions();
        configure?.Invoke(integrationsOptions);
        integrationsOptions.Validate();
        services.TryAddSingleton(integrationsOptions);
        services.TryAddSingleton(new IntegrationsSettings());

        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<IntegrationsDbContext>(options => options.UseIntegrationsNpgsql(connectionString));
        services.AddScoped<IWebhookEndpointRepository, WebhookEndpointRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
        services.AddScoped<IApiCredentialRepository, ApiCredentialRepository>();
        services.AddScoped<IApiCredentialLookup, ApiCredentialLookup>();
        services.AddScoped<IApiCredentialUsage, ApiCredentialUsage>();
        services.TryAddScoped<IIntegrationsActor, NoIntegrationsActor>();
        services.AddScoped<IWebhookWorkQueue, WebhookWorkQueue>();
        services.AddScoped<IIntegrationsUnitOfWork, IntegrationsUnitOfWork>();

        services.TryAddSingleton<WebhookSecretCipher>();
        services.TryAddScoped<IWebhookSecretVault, DatabaseWebhookSecretVault>();
        services.TryAddSingleton<IWebhookSender, SafeWebhookSender>();

        services.AddSingleton<WebhookDeliveryWorker>();
        if (integrationsOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<WebhookDeliveryWorker>());
        }

        services.AddOutboxDispatcher<IntegrationsDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseIntegrationsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}

using Integrations.Application;
using Integrations.Application.Abstractions;
using Integrations.Infrastructure;
using Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Integrations.Api;

/// <summary>
/// Composition du module Integrations, appelée par l'hôte. L'hôte fournit aussi <c>IIntegrationTenants</c> (Tenants), le
/// tenant courant (<c>AddRequestTenantContext</c>), le catalogue des scopes (<see cref="IntegrationsSettings.ApiScopes"/>),
/// le schéma d'authentification par clé (<see cref="ApiKeyAuthentication.AddIntegrationsApiKeys"/>) et la traduction
/// des événements des autres modules en webhooks (consommateur d'outbox).
/// </summary>
public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(
        this IServiceCollection services,
        string connectionString,
        Action<IntegrationsOptions>? configure = null,
        Action<IntegrationsSettings>? settings = null)
    {
        var integrationsSettings = new IntegrationsSettings();
        settings?.Invoke(integrationsSettings);
        services.TryAddSingleton(integrationsSettings);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IIntegrationsActor, HttpIntegrationsActor>();
        services.AddIntegrationsInfrastructure(connectionString, configure);
        services.AddScoped<IntegrationsWorkspace>();
        services.AddHandlersFrom(typeof(IntegrationsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateIntegrationsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

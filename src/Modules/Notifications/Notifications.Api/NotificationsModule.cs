using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Application.Sending;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Notifications.Api;

/// <summary>
/// Composition du module Notifications, appelée par l'hôte. L'hôte fournit aussi <c>INotificationTenants</c> (Tenants),
/// <c>INotificationLocations</c> (Locations), <c>INotificationContacts</c> et <c>INotificationConsents</c> (Customers), le
/// tenant courant (<c>AddRequestTenantContext</c>) et, s'il le souhaite, les quotas (Billing).
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        string connectionString,
        Action<NotificationsOptions>? configure = null,
        Action<NotificationsSettings>? settings = null)
    {
        var applicationSettings = new NotificationsSettings();
        settings?.Invoke(applicationSettings);
        services.TryAddSingleton(applicationSettings);
        services.AddHttpContextAccessor();
        services.TryAddScoped<INotificationsCurrentUser, HttpCurrentUser>();

        services.AddNotificationsInfrastructure(connectionString, configure);
        services.AddScoped<NotificationsWorkspace>();
        services.AddScoped<NotificationRouting>();
        services.AddScoped<NotificationPolicy>();
        services.AddScoped<NotificationSending>();
        services.AddScoped<NotificationRequests>();
        services.AddScoped<CredentialsCheck>();
        services.TryAddScoped<ICampaignAudience, NoCampaignAudience>();
        services.AddHandlersFrom(typeof(NotificationsApplicationErrors).Assembly);
        services.AddConfigurationTestRateLimiting();
        return services;
    }

    /// <summary>
    /// Applique les migrations en attente du schéma du module, puis pose le prestataire interne IN_APP et les templates
    /// de la plateforme des codes à usage unique s'ils manquent.
    /// </summary>
    public static async Task MigrateNotificationsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
        }

        await services.SeedBuiltInNotificationsAsync(cancellationToken);
    }

    /// <summary>Sans module Customers branché par l'hôte, une campagne n'atteint personne.</summary>
    private sealed class NoCampaignAudience : ICampaignAudience
    {
        public Task<int> CountAsync(TenantId tenantId, CommunicationChannel channel, string? tag, LocationId? customersOf = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<CustomerId>> ListAsync(
            TenantId tenantId, CommunicationChannel channel, string? tag, int skip, int take, LocationId? customersOf = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerId>>([]);
    }

    /// <summary>L'utilisateur de l'access token (claim <c>sub</c>) ; null hors requête ou sans utilisateur.</summary>
    private sealed class HttpCurrentUser(IHttpContextAccessor accessor) : INotificationsCurrentUser
    {
        public UserId? UserId =>
            Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
                ? SaasRestaurant.BuildingBlocks.Domain.UserId.From(id)
                : null;
    }
}

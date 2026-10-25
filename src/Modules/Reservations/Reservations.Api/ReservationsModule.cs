using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reservations.Application;
using Reservations.Infrastructure;
using Reservations.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reservations.Api;

/// <summary>
/// Composition du module Reservations, appelée par l'hôte. L'hôte fournit aussi <c>IReservationTenants</c> (module
/// Tenants), <c>IReservationLocations</c> (module Locations), <c>IReservationFloor</c> (module Floor) et le tenant de la
/// requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class ReservationsModule
{
    public static IServiceCollection AddReservationsModule(
        this IServiceCollection services, string connectionString, Action<ReservationsOptions>? configure = null)
    {
        services.AddReservationsInfrastructure(connectionString, configure);
        services.AddScoped<ReservationsWorkspace>();
        services.TryAddScoped<IPlanFeatureGate, AllowAllPlanFeatures>();
        services.AddHandlersFrom(typeof(ReservationsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateReservationsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReservationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

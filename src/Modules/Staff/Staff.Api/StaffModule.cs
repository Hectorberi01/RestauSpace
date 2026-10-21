using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;
using Staff.Application;
using Staff.Application.Commands;
using Staff.Infrastructure;
using Staff.Infrastructure.Persistence;

namespace Staff.Api;

/// <summary>
/// Composition du module Staff, appelée par l'hôte. L'hôte fournit aussi <c>IStaffTenants</c> (Tenants),
/// <c>ILocationDirectory</c> (Locations) et <c>IUserAccounts</c> (Identity).
/// </summary>
public static class StaffModule
{
    /// <param name="modulePermissions">
    /// Codes de permission déclarés par les AUTRES modules (ex. <c>locations.manage</c>) ; Staff ajoute les siens.
    /// Ensemble, ce sont les seules permissions qu'un rôle peut recevoir.
    /// </param>
    public static IServiceCollection AddStaffModule(
        this IServiceCollection services, string connectionString, IEnumerable<string> modulePermissions)
    {
        ArgumentNullException.ThrowIfNull(modulePermissions);

        services.AddHttpContextAccessor();
        services.TryAddScoped<Staff.Application.Abstractions.ITillClient, HttpTillClient>();
        services.TryAddScoped<Staff.Application.Abstractions.IOperatorSessions, NoOperatorSessions>();
        services.AddStaffInfrastructure(connectionString);
        services.AddScoped<TillDesk>();
        services.AddTillRateLimiting();
        services.AddSingleton(KnownPermissions.Of(modulePermissions));
        services.AddScoped<StaffWorkspace>();
        services.AddScoped<Staff.Application.Scheduling.SchedulingDesk>();
        services.AddScoped<IPermissionChecker, StaffPermissionChecker>();
        services.AddHandlersFrom(typeof(StaffApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Tant que l'hôte n'a pas branché Identity : aucune session n'est ouverte sur une caisse partagée.</summary>
    private sealed class NoOperatorSessions : Staff.Application.Abstractions.IOperatorSessions
    {
        public Task<Staff.Application.Abstractions.OperatorTokens?> StartAsync(UserId userId, TenantId tenantId, string device, CancellationToken cancellationToken = default) =>
            Task.FromResult<Staff.Application.Abstractions.OperatorTokens?>(null);
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateStaffDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StaffDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<CompleteSystemRolesCommand, int>>()
            .HandleAsync(new CompleteSystemRolesCommand(), cancellationToken);
    }
}

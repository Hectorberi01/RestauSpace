using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reporting.Application;
using Reporting.Application.Abstractions;
using Reporting.Infrastructure;
using Reporting.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reporting.Api;

/// <summary>
/// Composition du module Reporting, appelée par l'hôte. L'hôte fournit aussi <c>IReportingTenants</c> (Tenants), le
/// tenant courant (<c>AddRequestTenantContext</c>) et l'alimentation des compteurs (événements des autres modules,
/// par l'outbox).
/// </summary>
public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        string connectionString,
        Action<ReportingOptions>? configure = null,
        Action<ReportingSettings>? settings = null)
    {
        var reportingSettings = new ReportingSettings();
        settings?.Invoke(reportingSettings);
        services.TryAddSingleton(reportingSettings);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IReportingCurrentUser, HttpCurrentUser>();

        services.AddReportingInfrastructure(connectionString, configure);
        services.AddScoped<ReportingWorkspace>();
        services.AddHandlersFrom(typeof(ReportingApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateReportingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>L'utilisateur de l'access token (claim <c>sub</c>) ou la clé d'API (claim <c>cid</c>) ; null hors requête.</summary>
    private sealed class HttpCurrentUser(IHttpContextAccessor accessor) : IReportingCurrentUser
    {
        public UserId? UserId =>
            Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
                ? SaasRestaurant.BuildingBlocks.Domain.UserId.From(id)
                : null;

        public Guid? ApiCredentialId =>
            accessor.HttpContext?.User is { } user && ApiKeyClaims.IsApiKey(user)
            && Guid.TryParse(user.FindFirst(ApiKeyClaims.CredentialId)?.Value, out var id) && id != Guid.Empty
                ? id
                : null;
    }
}

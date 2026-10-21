using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reviews.Application;
using Reviews.Application.Abstractions;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reviews.Api;

/// <summary>
/// Composition du module Reviews, appelée par l'hôte. L'hôte fournit aussi <c>IReviewTenants</c> (Tenants),
/// <c>IReviewLocations</c> (Locations), <c>IReviewOrders</c> (Orders), <c>IReviewCustomers</c> (Customers) et le tenant
/// de la requête (<c>AddRequestTenantContext</c>).
/// </summary>
public static class ReviewsModule
{
    public static IServiceCollection AddReviewsModule(this IServiceCollection services, string connectionString)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IReviewsCurrentUser, HttpCurrentUser>();
        services.AddReviewsInfrastructure(connectionString);
        services.AddScoped<ReviewsWorkspace>();
        services.AddHandlersFrom(typeof(ReviewsApplicationErrors).Assembly);
        return services;
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateReviewsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>L'utilisateur de l'access token (claim <c>sub</c>) ; null pour une clé d'API ou hors requête.</summary>
    private sealed class HttpCurrentUser(IHttpContextAccessor accessor) : IReviewsCurrentUser
    {
        public UserId? UserId =>
            Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
                ? SaasRestaurant.BuildingBlocks.Domain.UserId.From(id)
                : null;
    }
}

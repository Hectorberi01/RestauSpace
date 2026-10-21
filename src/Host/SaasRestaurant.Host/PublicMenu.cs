using System.Threading.RateLimiting;
using Catalog.Application.Queries;
using Locations.Application.Queries;
using Microsoft.AspNetCore.Http.HttpResults;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

/// <summary>Ce que voit le client qui scanne le QR code d'une table : l'enseigne, l'établissement et ses menus publiés.</summary>
public sealed record PublicMenuResponse(
    string Restaurant,
    string? LogoUrl,
    string? PrimaryColor,
    string Location,
    string City,
    bool IsOpen,
    bool PricesIncludeTax,
    IReadOnlyList<PublicMenuDto> Menus);

/// <summary>
/// <c>GET /public/locations/{locationId}/menu</c> : anonyme, en lecture seule, limité en débit par adresse client et par établissement (le site public, qui appelle depuis son serveur, partage une seule adresse). Le
/// tenant est celui de l'établissement (lu côté serveur), jamais une valeur de la requête. Un établissement inconnu,
/// archivé, ou d'un restaurant suspendu ou fermé répond 404 sans rien révéler. Aucune donnée client ni id interne.
/// </summary>
internal static class PublicMenuEndpoints
{
    public const string RateLimitPolicy = "public-menu";
    public const string MediaRateLimitPolicy = "public-media";

    public static IServiceCollection AddPublicMenuRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            $"{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{httpContext.Request.RouteValues["locationId"]}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }))
            // Photos publiques : une page de catalogue en charge des dizaines ; le CDN absorbe ensuite l'essentiel.
            .AddPolicy(MediaRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

    public static RouteHandlerBuilder MapPublicMenuEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/public/locations/{locationId:guid}/menu", async Task<Results<Ok<PublicMenuResponse>, NotFound>> (
                Guid locationId,
                HttpContext http,
                IQueryHandler<GetPublicLocationQuery, PublicLocationDto?> locations,
                IQueryHandler<GetPublicMenuQuery, IReadOnlyList<PublicMenuDto>> menus,
                CachedTenants tenants,
                CancellationToken ct) =>
            {
                var location = await locations.HandleAsync(new GetPublicLocationQuery(locationId), ct);
                if (location is null)
                {
                    return TypedResults.NotFound();
                }

                var tenant = await tenants.FindAsync(TenantId.From(location.TenantId), ct);
                if (tenant is not { CanAcceptBusinessWrites: true })
                {
                    return TypedResults.NotFound();
                }

                var published = await menus.HandleAsync(new GetPublicMenuQuery(location.TenantId, location.LocationId), ct);
                http.Response.Headers.CacheControl = "public, max-age=60";
                return TypedResults.Ok(new PublicMenuResponse(
                    string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName,
                    tenant.Branding.LogoUrl,
                    tenant.Branding.PrimaryColor,
                    location.Name,
                    location.City,
                    location.IsOpenNow,
                    tenant.PricesIncludeTax,
                    published));
            })
            .WithTags("Public")
            .RequireRateLimiting(RateLimitPolicy);
}

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;
using Tenants.Application.Prospects;
using Tenants.Domain.Prospects;

namespace Tenants.Api;

/// <summary>
/// Formulaire du site vitrine. <see cref="Website"/> est un piège à robots : invisible pour un visiteur, il est rempli
/// par les robots qui remplissent tous les champs ; la demande est alors ignorée sans le leur dire.
/// </summary>
public sealed record SubmitDemoRequest(
    string? ContactName,
    string? RestaurantName,
    string? Email,
    string? Phone,
    string? Country,
    string? PlanInterest,
    string? Message,
    string? Website);

public sealed record UpdateDemoRequestRequest(DemoRequestStatus Status, string? Note);

public sealed record DemoRequestRouteGroups(RouteGroupBuilder Public, RouteGroupBuilder Admin);

public sealed class DemoRequestRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;

    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}

public static class DemoRequestEndpoints
{
    public const string RateLimitPolicy = "tenants-demo-requests";

    /// <summary>
    /// Par adresse client : quelques demandes par heure suffisent à un prospect et freinent le spam. Derrière un proxy,
    /// l'hôte doit rétablir la vraie adresse client (forwarded headers).
    /// </summary>
    public static IServiceCollection AddDemoRequestRateLimiting(this IServiceCollection services, DemoRequestRateLimitOptions? options = null)
    {
        var settings = options ?? new DemoRequestRateLimitOptions();
        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.PermitLimit, Window = settings.Window, QueueLimit = 0 }));
        });
    }

    /// <summary>
    /// <c>POST /demo-requests</c> (public, anonyme, limité en débit) et <c>/admin/demo-requests</c> (back-office) :
    /// l'hôte pose l'autorisation de chaque groupe.
    /// </summary>
    public static DemoRequestRouteGroups MapDemoRequestEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapPublic(routes), MapAdmin(routes));

    private static RouteGroupBuilder MapPublic(IEndpointRouteBuilder routes)
    {
        var demo = routes.MapGroup("/demo-requests").WithTags("Demo requests");

        demo.MapPost("/", async (SubmitDemoRequest request, ICommandHandler<SubmitDemoRequestCommand, Guid> handler, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(request.Website))
            {
                return TypedResults.Accepted((string?)null);
            }

            await handler.HandleAsync(
                new SubmitDemoRequestCommand(
                    request.ContactName, request.RestaurantName, request.Email, request.Phone, request.Country, request.PlanInterest,
                    request.Message),
                ct);
            // Rien n'est renvoyé : l'identifiant ne sert qu'au back-office.
            return TypedResults.Accepted((string?)null);
        })
        .RequireRateLimiting(RateLimitPolicy);

        return demo;
    }

    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/demo-requests").WithTags("Demo requests administration");

        admin.MapGet("/", async (
            DemoRequestStatus? status, int? page, int? pageSize,
            IQueryHandler<ListDemoRequestsQuery, IReadOnlyList<DemoRequestDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListDemoRequestsQuery(status, skip, take), ct))));

        admin.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetDemoRequestQuery, DemoRequestDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDemoRequestQuery(id), ct)));

        admin.MapPut("/{id:guid}", async (
            Guid id, UpdateDemoRequestRequest request, ICommandHandler<UpdateDemoRequestCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdateDemoRequestCommand(id, request.Status, request.Note), ct);
            return TypedResults.NoContent();
        });

        admin.MapDelete("/{id:guid}", async (Guid id, ICommandHandler<DeleteDemoRequestCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DeleteDemoRequestCommand(id), ct);
            return TypedResults.NoContent();
        });

        return admin;
    }
}

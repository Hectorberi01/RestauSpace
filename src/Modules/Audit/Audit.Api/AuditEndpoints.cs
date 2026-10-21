using System.Security.Claims;
using Audit.Application;
using Audit.Application.Queries;
using Audit.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Audit.Api;

/// <summary>Les deux groupes de routes : l'hôte chaîne la policy de chacun (restaurant, administration).</summary>
public sealed record AuditRouteGroups(RouteGroupBuilder Restaurant, RouteGroupBuilder Admin);

/// <summary>
/// Consultation du journal d'audit, en LECTURE SEULE : aucune route n'écrit, ne modifie ni ne supprime une écriture.
/// <list type="bullet">
/// <item><c>/audit</c> : le restaurant sélectionné dans l'access token (jamais un tenant de requête), permission
/// <see cref="AuditPermissions.View"/> ;</item>
/// <item><c>/admin/audit</c> : la plateforme (tout le journal, un restaurant, ou les écritures sans restaurant).</item>
/// </list>
/// Filtres : <c>module</c>, <c>action</c>, <c>targetType</c>, <c>targetId</c>, <c>actorType</c> + <c>actorId</c>,
/// <c>from</c> (inclus), <c>to</c> (exclu), <c>importance</c> (High, Medium, Low), <c>page</c>, <c>pageSize</c> (100 au plus).
/// </summary>
public static class AuditEndpoints
{
    public static AuditRouteGroups MapAuditEndpoints(this IEndpointRouteBuilder routes) => new(MapRestaurant(routes), MapAdmin(routes));

    private static RouteGroupBuilder MapRestaurant(IEndpointRouteBuilder routes)
    {
        var audit = routes.MapGroup("/audit").WithTags("Audit");

        audit.MapGet("/", async (
            string? module, string? action, string? targetType, string? targetId, ActorType? actorType, string? actorId,
            DateTimeOffset? from, DateTimeOffset? to, string? importance, int? page, int? pageSize,
            IQueryHandler<SearchAuditEntriesQuery, IReadOnlyList<AuditEntryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new SearchAuditEntriesQuery(new AuditFilter(module, action, targetType, targetId, actorType, actorId, from, to, importance), skip, take),
                ct))))
            .RequirePermission(AuditPermissions.View);

        // Chiffres de la période (bornes obligatoires) avec les mêmes filtres ; utcOffsetMinutes : jours du navigateur.
        audit.MapGet("/summary", async (
            string? module, string? action, string? targetType, string? targetId, ActorType? actorType, string? actorId,
            DateTimeOffset? from, DateTimeOffset? to, string? importance, int? utcOffsetMinutes,
            IQueryHandler<GetAuditSummaryQuery, AuditSummaryDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new GetAuditSummaryQuery(new AuditFilter(module, action, targetType, targetId, actorType, actorId, from, to, importance), utcOffsetMinutes ?? 0),
                ct)))
            .RequirePermission(AuditPermissions.View);

        // Ses propres actions : aucune permission, l'utilisateur est celui du token (claim sub), jamais une valeur de requête.
        audit.MapGet("/me", async (
            int? limit, ClaimsPrincipal caller, IQueryHandler<ListMyActivityQuery, IReadOnlyList<AuditEntryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new ListMyActivityQuery(Guid.TryParse(caller.FindFirst("sub")?.Value, out var user) ? user : Guid.Empty, Math.Clamp(limit ?? 10, 1, 50)),
                ct)));

        audit.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetAuditEntryQuery, AuditEntryDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetAuditEntryQuery(id), ct)))
            .RequirePermission(AuditPermissions.View);

        return audit;
    }

    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/audit").WithTags("Audit administration");

        admin.MapGet("/", async (
            Guid? tenantId, bool? platformOnly,
            string? module, string? action, string? targetType, string? targetId, ActorType? actorType, string? actorId,
            DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            IQueryHandler<SearchPlatformAuditEntriesQuery, IReadOnlyList<AuditEntryDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new SearchPlatformAuditEntriesQuery(
                    tenantId, platformOnly ?? false, new AuditFilter(module, action, targetType, targetId, actorType, actorId, from, to), skip, take),
                ct))));

        admin.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPlatformAuditEntryQuery, AuditEntryDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPlatformAuditEntryQuery(id), ct)));

        return admin;
    }
}

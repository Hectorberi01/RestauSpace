using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reporting.Application;
using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Queries;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reporting.Api;

/// <summary>Les deux groupes de routes : l'hôte chaîne la policy de chacun (restaurant, administration).</summary>
public sealed record ReportingRouteGroups(RouteGroupBuilder Restaurant, RouteGroupBuilder Admin);

/// <summary>
/// Rapports et tableaux de bord.
/// <list type="bullet">
/// <item><c>/reporting</c> : le restaurant sélectionné dans l'access token (jamais un tenant de requête). Lecture,
/// exécution et téléchargement : <see cref="ReportingPermissions.View"/> ; rapports enregistrés et planification :
/// <see cref="ReportingPermissions.Manage"/> ; demande d'export : <see cref="ReportingPermissions.Export"/> ;</item>
/// <item><c>/admin/reporting</c> : la plateforme (restaurants, abonnements, MRR, activité de tous les restaurants).</item>
/// </list>
/// Les compteurs sont alimentés par l'hôte (événements des autres modules), sans route.
/// </summary>
public static class ReportingEndpoints
{
    private const string View = ReportingPermissions.View;
    private const string Manage = ReportingPermissions.Manage;
    private const string Export = ReportingPermissions.Export;
    private const string GetDefinitionRoute = "GetReportDefinition";
    private const string GetExportRoute = "GetReportExport";

    public static ReportingRouteGroups MapReportingEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapRestaurant(routes), MapAdmin(routes));

    private static RouteGroupBuilder MapRestaurant(IEndpointRouteBuilder routes)
    {
        var reporting = routes.MapGroup("/reporting").WithTags("Reporting");

        reporting.MapGet("/catalog", async (IQueryHandler<GetReportCatalogQuery, ReportCatalogDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReportCatalogQuery(), ct)))
            .RequirePermission(View);

        reporting.MapGet("/dashboard", async (
            DateOnly from, DateOnly to, Guid? locationId, IQueryHandler<GetDashboardQuery, DashboardDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDashboardQuery(from, to, locationId), ct)))
            .RequirePermission(View);

        reporting.MapPost("/run", async (RunReportRequest request, IQueryHandler<RunReportQuery, ReportResultDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new RunReportQuery(request.Metrics, request.Filters, request.From, request.To, request.Granularity, request.ByLocation), ct)))
            .RequirePermission(View);

        MapDefinitions(reporting.MapGroup("/definitions"));
        MapExports(reporting.MapGroup("/exports"));
        return reporting;
    }

    private static void MapDefinitions(RouteGroupBuilder definitions)
    {
        definitions.MapGet("/", async (IQueryHandler<ListReportDefinitionsQuery, IReadOnlyList<ReportDefinitionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListReportDefinitionsQuery(), ct)))
            .RequirePermission(View);

        definitions.MapPost("/", async (CreateReportDefinitionRequest request, ICommandHandler<CreateReportDefinitionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateReportDefinitionCommand(request.Name, request.Metrics, request.Filters), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetDefinitionRoute, new { id });
        }).RequirePermission(Manage);

        definitions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetReportDefinitionQuery, ReportDefinitionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetReportDefinitionQuery(id), ct)))
            .WithName(GetDefinitionRoute)
            .RequirePermission(View);

        definitions.MapPut("/{id:guid}/name", (Guid id, RenameReportRequest request, ICommandHandler<RenameReportDefinitionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameReportDefinitionCommand(id, request.Name), ct)))
            .RequirePermission(Manage);

        definitions.MapPut("/{id:guid}/filters", (Guid id, ReportFiltersRequest request, ICommandHandler<ChangeReportFiltersCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeReportFiltersCommand(id, request.Filters), ct)))
            .RequirePermission(Manage);

        definitions.MapPost("/{id:guid}/run", async (
            Guid id, ReportPeriodRequest request, IQueryHandler<RunSavedReportQuery, ReportResultDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RunSavedReportQuery(id, request.From, request.To, request.Granularity, request.ByLocation), ct)))
            .RequirePermission(View);

        definitions.MapPut("/{id:guid}/schedule", (Guid id, ScheduleReportRequest request, ICommandHandler<ScheduleReportCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new ScheduleReportCommand(id, request.Frequency, request.DayOfWeek, request.DayOfMonth, request.LocalTime, request.Recipients), ct)))
            .RequirePermission(Manage);

        definitions.MapDelete("/{id:guid}/schedule", (Guid id, ICommandHandler<UnscheduleReportCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UnscheduleReportCommand(id), ct)))
            .RequirePermission(Manage);
    }

    private static void MapExports(RouteGroupBuilder exports)
    {
        exports.MapPost("/", async (RequestExportRequest request, ICommandHandler<RequestExportCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new RequestExportCommand(
                    request.DefinitionId, request.Metrics, request.Filters, request.From, request.To, request.Granularity, request.ByLocation,
                    request.Format ?? ExportFormat.Csv),
                ct);
            return TypedResults.AcceptedAtRoute(new CreatedResponse(id), GetExportRoute, new { id });
        }).RequirePermission(Export);

        // Avant « /{id} » : route fixe. Journal comptable (ventes et trésorerie) de la période.
        exports.MapPost("/accounting", async (AccountingExportRequest request, ICommandHandler<RequestAccountingExportCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new RequestAccountingExportCommand(request.From, request.To, request.Plan), ct);
            return TypedResults.AcceptedAtRoute(new CreatedResponse(id), GetExportRoute, new { id });
        }).RequirePermission(Export);

        exports.MapGet("/", async (int? page, int? pageSize, IQueryHandler<ListExportsQuery, IReadOnlyList<ExportJobDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListExportsQuery(skip, take), ct))))
            .RequirePermission(View);

        exports.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetExportQuery, ExportJobDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetExportQuery(id), ct)))
            .WithName(GetExportRoute)
            .RequirePermission(View);

        exports.MapGet("/{id:guid}/file", async (Guid id, IQueryHandler<GetExportFileQuery, ExportFileDto> handler, CancellationToken ct) =>
        {
            var file = await handler.HandleAsync(new GetExportFileQuery(id), ct);
            return TypedResults.File(file.Content, file.ContentType, file.FileName);
        }).RequirePermission(View);
    }

    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/reporting").WithTags("Reporting administration");

        admin.MapGet("/dashboard", async (
            DateOnly from, DateOnly to, IQueryHandler<GetPlatformDashboardQuery, PlatformDashboardDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPlatformDashboardQuery(from, to), ct)));

        // Mesures séparées par des virgules : ?metrics=sales.revenue,sales.orders_completed
        admin.MapGet("/activity", async (
            DateOnly from, DateOnly to, ReportGranularity? granularity, string? metrics,
            IQueryHandler<GetPlatformActivityQuery, ReportResultDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new GetPlatformActivityQuery(
                    (metrics ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    from,
                    to,
                    granularity ?? ReportGranularity.Day),
                ct)));

        admin.MapGet("/tenant-activity", async (
            DateOnly from, DateOnly to, IQueryHandler<GetPlatformTenantActivityQuery, PlatformTenantActivityDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPlatformTenantActivityQuery(from, to), ct)));

        admin.MapGet("/tenants", async (
            PlatformTenantStatus? status, int? page, int? pageSize,
            IQueryHandler<ListPlatformTenantsQuery, IReadOnlyList<PlatformTenantRow>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListPlatformTenantsQuery(status, skip, take), ct))));

        return admin;
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}

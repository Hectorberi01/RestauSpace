using System.Security.Claims;
using Backups.Application;
using Backups.Application.Commands;
using Backups.Application.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Backups.Api;

public sealed record DownloadLinkRequest(string? Code)
{
    public override string ToString() => "DownloadLinkRequest (***)";
}

public sealed record RestoreBackupRequest(string? Code, string? ConfirmDatabaseName)
{
    public override string ToString() => "RestoreBackupRequest (***)";
}

/// <summary>
/// Sauvegardes de la base, sous <c>/admin/backups</c> : l'hôte y pose la policy des administrateurs de la plateforme.
/// L'API enregistre des demandes ; l'agent du serveur de base les exécute.
/// </summary>
public static class BackupsEndpoints
{
    public static RouteGroupBuilder MapBackupsEndpoints(this IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/backups").WithTags("Backups administration");

        admin.MapGet("/", async (int? page, int? pageSize, IQueryHandler<ListBackupsQuery, PagedResult<BackupDto>> handler, CancellationToken ct) =>
        {
            var request = PageRequest.From(page, pageSize);
            return TypedResults.Ok(await handler.HandleAsync(new ListBackupsQuery(request.Page, request.PageSize), ct));
        });

        admin.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetBackupQuery, BackupDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetBackupQuery(id), ct)));

        admin.MapPost("/", async (ClaimsPrincipal caller, ICommandHandler<RequestBackupCommand, BackupDto> handler, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, await handler.HandleAsync(new RequestBackupCommand(UserOf(caller)), ct)));

        admin.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal caller, ICommandHandler<DeleteBackupCommand, BackupDto> handler, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, await handler.HandleAsync(new DeleteBackupCommand(id, UserOf(caller)), ct)));

        admin.MapPost("/{id:guid}/download-link", async (
            Guid id, DownloadLinkRequest? request, ClaimsPrincipal caller,
            ICommandHandler<CreateDownloadLinkCommand, BackupDownloadLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new CreateDownloadLinkCommand(id, UserOf(caller), request?.Code), ct)));

        admin.MapPost("/{id:guid}/restore", async (
            Guid id, RestoreBackupRequest? request, ClaimsPrincipal caller,
            ICommandHandler<RequestRestoreCommand, RestoreDto> handler, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, await handler.HandleAsync(
                new RequestRestoreCommand(id, UserOf(caller), request?.Code, request?.ConfirmDatabaseName), ct)));

        admin.MapGet("/restores", async (int? page, int? pageSize, IQueryHandler<ListRestoresQuery, PagedResult<RestoreDto>> handler, CancellationToken ct) =>
        {
            var request = PageRequest.From(page, pageSize);
            return TypedResults.Ok(await handler.HandleAsync(new ListRestoresQuery(request.Page, request.PageSize), ct));
        });

        admin.MapPost("/restores/{id:guid}/cancel", async (
            Guid id, ClaimsPrincipal caller, ICommandHandler<CancelRestoreCommand, RestoreDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new CancelRestoreCommand(id, UserOf(caller)), ct)));

        admin.MapGet("/agent", async (IQueryHandler<GetBackupAgentQuery, BackupAgentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetBackupAgentQuery(), ct)));

        admin.MapGet("/settings", async (IQueryHandler<GetBackupSettingsQuery, BackupSettingsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetBackupSettingsQuery(), ct)));

        return admin;
    }

    private static Guid UserOf(ClaimsPrincipal caller) =>
        Guid.TryParse(caller.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new UnauthenticatedException(BackupsApplicationErrors.InvalidAccessToken);
}

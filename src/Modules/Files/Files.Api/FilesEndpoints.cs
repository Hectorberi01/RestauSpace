using Files.Application;
using Files.Application.Commands;
using Files.Application.Queries;
using Files.Application.Storage;
using Files.Domain;
using Files.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Files.Api;

/// <summary>
/// Fichiers du restaurant sélectionné dans l'access token (jamais un tenant de requête). L'hôte chaîne
/// <c>Tenancy.TenantMemberPolicy</c> sur le groupe. Les octets ne passent jamais par l'API : envoi et téléchargement se
/// font directement avec le stockage, par des URLs signées et courtes. Lecture : <see cref="FilesPermissions.View"/> ;
/// téléversement : <see cref="FilesPermissions.Upload"/> ; suppression : <see cref="FilesPermissions.Manage"/>.
/// </summary>
public static class FilesEndpoints
{
    private const string View = FilesPermissions.View;
    private const string Upload = FilesPermissions.Upload;
    private const string Manage = FilesPermissions.Manage;
    private const string GetFileRoute = "GetMediaFile";

    public static RouteGroupBuilder MapFilesEndpoints(this IEndpointRouteBuilder routes)
    {
        var files = routes.MapGroup("/files").WithTags("Files");

        files.MapPost("/uploads", async (RequestUploadRequest request, ICommandHandler<RequestUploadCommand, UploadTicket> handler, CancellationToken ct) =>
        {
            var ticket = await handler.HandleAsync(
                new RequestUploadCommand(request.OwnerType, request.OwnerId, request.FileName, request.ContentType, request.SizeInBytes, request.ThumbnailOf), ct);
            return TypedResults.CreatedAtRoute(ticket, GetFileRoute, new { id = ticket.FileId });
        }).RequirePermission(Upload);

        files.MapPost("/{id:guid}/complete", async (Guid id, ICommandHandler<CompleteUploadCommand, UploadResult> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new CompleteUploadCommand(id), ct)))
            .RequirePermission(Upload);

        files.MapGet("/", async (string? ownerType, Guid ownerId, IQueryHandler<ListOwnerFilesQuery, IReadOnlyList<MediaFileDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListOwnerFilesQuery(ownerType, ownerId), ct)))
            .RequirePermission(View);

        files.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetFileQuery, MediaFileDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetFileQuery(id), ct)))
            .WithName(GetFileRoute)
            .RequirePermission(View);

        // Le lien est renvoyé (et non une redirection) : le client choisit quand l'ouvrir, il expire vite.
        files.MapGet("/{id:guid}/download", async (Guid id, IQueryHandler<GetDownloadLinkQuery, DownloadLinkDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetDownloadLinkQuery(id), ct)))
            .RequirePermission(View);

        files.MapGet("/{id:guid}/public-link", async (Guid id, IQueryHandler<GetPublicLinkQuery, PublicImageDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPublicLinkQuery(id), ct)))
            .RequirePermission(View);

        files.MapDelete("/{id:guid}", async (Guid id, ICommandHandler<DeleteFileCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new DeleteFileCommand(id), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        return files;
    }

    /// <summary>
    /// Envoi (PUT) et lecture (GET) des octets du stockage local de développement (<see cref="LocalObjectStorage"/>).
    /// Anonymes comme une URL pré-signée de R2 : l'autorisation EST la signature du jeton, qui fixe la méthode, l'objet, le
    /// type et la taille exacts. L'hôte ne les mappe que si ce stockage est actif.
    /// </summary>
    public static RouteGroupBuilder MapFilesLocalStorageEndpoints(this IEndpointRouteBuilder routes)
    {
        var local = routes.MapGroup("/files/local").WithTags("Files").ExcludeFromDescription();

        local.MapPut("/{token}", async (string token, HttpRequest request, LocalObjectStorage storage, CancellationToken ct) =>
        {
            var grant = storage.Verify(token, "PUT");
            if (grant is null)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var declared = request.ContentType?.Split(';')[0].Trim();
            if (grant.SizeInBytes is not { } size
                || !string.Equals(declared, grant.ContentType, StringComparison.OrdinalIgnoreCase)
                || (request.ContentLength is { } length && length != size))
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var content = new byte[size];
            var read = 0;
            while (read < size)
            {
                var count = await request.Body.ReadAsync(content.AsMemory(read), ct);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read != size || await request.Body.ReadAsync(new byte[1], ct) != 0)
            {
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            await storage.WriteAsync(grant.Key, content, ct);
            return Results.Ok();
        });

        local.MapGet("/{token}", async (string token, HttpContext context, LocalObjectStorage storage, CancellationToken ct) =>
        {
            var grant = storage.Verify(token, "GET");
            if (grant is null)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var content = await storage.GetAsync(grant.Key, ct);
            if (content is null)
            {
                return Results.NotFound();
            }

            // Type reconnu aux premiers octets, comme à la confirmation de l'envoi ; une image s'affiche, le reste se télécharge.
            var type = ContentSniffer.Detect(
                content.AsSpan(0, Math.Min(content.Length, ContentSniffer.HeadLength)), ContentType.Create("application/octet-stream"));
            context.Response.Headers.CacheControl = "private, max-age=300";
            return type.Type == "image" ? Results.File(content, type.Value) : Results.File(content, type.Value, grant.FileName);
        });

        return local;
    }

    /// <summary>
    /// <c>GET /media/{jeton}</c> : une photo publique (catalogue en ligne, borne), anonyme. L'autorisation EST la signature
    /// du jeton (<see cref="PublicMediaLinks"/>), qui fixe le tenant et le fichier. Un fichier ne change jamais : la
    /// réponse est cachée un an par le navigateur et les CDN. L'hôte chaîne l'accès anonyme et la limite de débit.
    /// </summary>
    public static RouteHandlerBuilder MapPublicMediaEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/media/{token}", async (string token, HttpContext context, IQueryHandler<ReadPublicMediaQuery, PublicMediaDto?> handler, CancellationToken ct) =>
        {
            var media = await handler.HandleAsync(new ReadPublicMediaQuery(token), ct);
            if (media is null)
            {
                return Results.NotFound();
            }

            var etag = $"\"{media.FileId:N}\"";
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            return context.Request.Headers.IfNoneMatch.Any(value => value == etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.File(media.Content, media.ContentType);
        })
        .WithTags("Files")
        .ExcludeFromDescription();
}

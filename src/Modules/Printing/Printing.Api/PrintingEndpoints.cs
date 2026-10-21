using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;
using Printing.Application;
using Printing.Application.Commands;
using Printing.Application.Queries;
using Printing.Application.Rendering;
using Printing.Domain;

namespace Printing.Api;

public sealed record CreatePrinterRequest(Guid LocationId, string? Name, PrinterProtocol Protocol, bool PrintsCustomerTickets = true);

public sealed record UpdatePrinterRequest(string? Name, bool PrintsCustomerTickets);

public sealed record PrinterKitchenRequest(bool PrintsKitchenTickets, Guid? KitchenStationId);

/// <param name="PrinterId">Facultatif : l'imprimante de tickets client de l'établissement de la commande par défaut.</param>
public sealed record OrderReceiptRequest(Guid? PrinterId);

public sealed record RelayAcknowledgement(bool Printed, string? Code = null);

/// <summary>Un ticket remis au relais ESC/POS : les octets à envoyer tels quels à l'imprimante (port 9100).</summary>
public sealed record RelayJob(Guid JobId, string Format, string Content);

/// <summary>
/// Trois groupes. <c>/printing</c> : la console (imprimantes, jetons, tickets), sous <c>Tenancy.TenantMemberPolicy</c>
/// chaînée par l'hôte ; lecture <see cref="PrintingPermissions.View"/>, gestion <see cref="PrintingPermissions.Manage"/>.
/// <c>/printing/cloudprnt/{jeton}</c> : le protocole Star CloudPRNT (POST : y a-t-il un ticket ? GET : le ticket ;
/// DELETE : l'accusé). <c>/printing/relay/{jeton}</c> : le relais ESC/POS. Ces deux derniers par jeton d'imprimante.
/// </summary>
public static class PrintingEndpoints
{
    private const string View = PrintingPermissions.View;
    private const string Manage = PrintingPermissions.Manage;
    private const string OrdersTake = "orders.take";

    public static (RouteGroupBuilder Console, RouteGroupBuilder Devices) MapPrintingEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return (MapConsole(routes), MapDevices(routes));
    }

    private static RouteGroupBuilder MapConsole(IEndpointRouteBuilder routes)
    {
        var printing = routes.MapGroup("/printing").WithTags("Printing");

        printing.MapGet("/printers", async (Guid? locationId, IQueryHandler<ListPrintersQuery, IReadOnlyList<PrinterDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPrintersQuery(locationId), ct)))
            .RequirePermission(View);

        printing.MapPost("/printers", async (CreatePrinterRequest request, ICommandHandler<CreatePrinterCommand, IssuedPrinterToken> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(new CreatePrinterCommand(request.LocationId, request.Name, request.Protocol, request.PrintsCustomerTickets), ct);
            return TypedResults.Created($"/printing/printers/{issued.Id}", issued);
        }).RequirePermission(Manage);

        printing.MapPut("/printers/{id:guid}", async (Guid id, UpdatePrinterRequest request, ICommandHandler<UpdatePrinterCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new UpdatePrinterCommand(id, request.Name, request.PrintsCustomerTickets), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        printing.MapPut("/printers/{id:guid}/kitchen", async (Guid id, PrinterKitchenRequest request, ICommandHandler<SetPrinterKitchenRoleCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPrinterKitchenRoleCommand(id, request.PrintsKitchenTickets, request.KitchenStationId), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        // Imprimer un reçu fait partie de la prise de commande (permission d'Orders), pas du réglage des imprimantes.
        printing.MapPost("/receipts/{orderId:guid}", async (Guid orderId, OrderReceiptRequest? request, ICommandHandler<PrintOrderReceiptCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new { jobId = await handler.HandleAsync(new PrintOrderReceiptCommand(orderId, request?.PrinterId), ct) }))
            .RequirePermission(OrdersTake);

        printing.MapPost("/printers/{id:guid}/token", async (Guid id, ICommandHandler<RotatePrinterTokenCommand, IssuedPrinterToken> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RotatePrinterTokenCommand(id), ct)))
            .RequirePermission(Manage);

        printing.MapPost("/printers/{id:guid}/disable", async (Guid id, ICommandHandler<SetPrinterDisabledCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPrinterDisabledCommand(id, true), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        printing.MapPost("/printers/{id:guid}/enable", async (Guid id, ICommandHandler<SetPrinterDisabledCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new SetPrinterDisabledCommand(id, false), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Manage);

        printing.MapPost("/printers/{id:guid}/test", async (Guid id, ICommandHandler<PrintTestPageCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new { jobId = await handler.HandleAsync(new PrintTestPageCommand(id), ct) }))
            .RequirePermission(Manage);

        printing.MapGet("/jobs", async (Guid? locationId, int? take, IQueryHandler<ListPrintJobsQuery, IReadOnlyList<PrintJobDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPrintJobsQuery(locationId, take ?? 50), ct)))
            .RequirePermission(View);

        printing.MapPost("/jobs/{id:guid}/reprint", async (Guid id, ICommandHandler<ReprintJobCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new { jobId = await handler.HandleAsync(new ReprintJobCommand(id), ct) }))
            .RequirePermission(Manage);

        return printing;
    }

    /// <summary>Le chemin d'une requête d'imprimante, jeton remplacé (pour les traces) ; null pour toute autre requête.</summary>
    public static string? MaskDevicePath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        foreach (var prefix in (string[])["/v1/printing/cloudprnt/", "/v1/printing/relay/"])
        {
            var start = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var tokenStart = start + prefix.Length;
                var tokenEnd = value.IndexOf('/', tokenStart);
                return value[..tokenStart] + "{printerToken}" + (tokenEnd < 0 ? string.Empty : value[tokenEnd..]);
            }
        }

        return null;
    }

    private static RouteGroupBuilder MapDevices(IEndpointRouteBuilder routes)
    {
        var devices = routes.MapGroup("/printing")
            .WithTags("Printing devices")
            .ExcludeFromDescription()
            .RequireAuthorization(PrinterTokenAuthentication.Policy)
            .RequireRateLimiting(PrinterTokenAuthentication.RateLimitPolicy);

        // Star CloudPRNT : l'imprimante annonce son état et demande s'il y a un ticket.
        devices.MapPost("/cloudprnt/{printerToken}", async (ICommandHandler<PeekPrintJobCommand, Guid?> handler, CancellationToken ct) =>
        {
            var job = await handler.HandleAsync(new PeekPrintJobCommand(), ct);
            return job is { } id
                ? Results.Json(new { jobReady = true, mediaTypes = TicketFormats.Star, jobToken = id.ToString("N") })
                : Results.Json(new { jobReady = false });
        });

        devices.MapGet("/cloudprnt/{printerToken}", async (string? type, string? token, ICommandHandler<FetchPrintJobCommand, PrintJobContent?> handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(token, out var jobId))
            {
                return Results.NotFound();
            }

            var content = await handler.HandleAsync(new FetchPrintJobCommand(jobId, type ?? TicketFormats.StarPrnt), ct);
            return content is null ? Results.NotFound() : Results.File(content.Content, content.ContentType);
        });

        // Accusé : un code commençant par 2 (« 200 OK ») = imprimé ; tout autre code = échec (papier, capot…).
        devices.MapDelete("/cloudprnt/{printerToken}", async (string? code, string? token, ICommandHandler<CompletePrintJobCommand> handler, CancellationToken ct) =>
        {
            if (!Guid.TryParse(token, out var jobId))
            {
                return Results.Ok();
            }

            try
            {
                await handler.HandleAsync(new CompletePrintJobCommand(jobId, code?.StartsWith('2') == true, code), ct);
            }
            catch (Exception exception) when (exception is DomainException or NotFoundException)
            {
                // Un accusé rejoué ou tardif ne doit pas faire réessayer l'imprimante indéfiniment.
            }

            return Results.Ok();
        });

        devices.MapGet("/relay/{printerToken}/next", async Task<Results<Ok<RelayJob>, NoContent>> (
            ICommandHandler<PeekPrintJobCommand, Guid?> peek, ICommandHandler<FetchPrintJobCommand, PrintJobContent?> fetch, CancellationToken ct) =>
        {
            if (await peek.HandleAsync(new PeekPrintJobCommand(), ct) is not { } jobId
                || await fetch.HandleAsync(new FetchPrintJobCommand(jobId, TicketFormats.EscPos), ct) is not { } content)
            {
                return TypedResults.NoContent();
            }

            return TypedResults.Ok(new RelayJob(content.JobId, content.ContentType, Convert.ToBase64String(content.Content)));
        });

        devices.MapPost("/relay/{printerToken}/jobs/{jobId:guid}", async (Guid jobId, RelayAcknowledgement request, ICommandHandler<CompletePrintJobCommand> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new CompletePrintJobCommand(jobId, request.Printed, request.Code), ct);
            return TypedResults.NoContent();
        });

        return devices;
    }
}

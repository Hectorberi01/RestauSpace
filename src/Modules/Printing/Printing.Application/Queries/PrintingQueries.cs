using Printing.Domain;

namespace Printing.Application.Queries;

/// <param name="IsOnline">L'imprimante (ou son relais) a interrogé l'API il y a moins de 2 minutes.</param>
public sealed record PrinterDto(
    Guid Id,
    Guid LocationId,
    string Name,
    PrinterProtocol Protocol,
    bool PrintsCustomerTickets,
    PrinterStatus Status,
    string TokenHint,
    DateTimeOffset? LastSeenAtUtc,
    bool IsOnline,
    DateTimeOffset CreatedAtUtc,
    bool PrintsKitchenTickets = false,
    Guid? KitchenStationId = null);

public sealed record PrintJobDto(
    Guid Id,
    Guid PrinterId,
    Guid LocationId,
    PrintJobKind Kind,
    string Reference,
    PrintJobStatus Status,
    int Attempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode);

/// <summary>Les imprimantes du restaurant ; <paramref name="LocationId"/> null = tous les établissements.</summary>
public sealed record ListPrintersQuery(Guid? LocationId = null) : IQuery<IReadOnlyList<PrinterDto>>;

public sealed class ListPrintersHandler(PrintingWorkspace workspace, IClock clock) : IQueryHandler<ListPrintersQuery, IReadOnlyList<PrinterDto>>
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    public async Task<IReadOnlyList<PrinterDto>> HandleAsync(ListPrintersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var location = query.LocationId is { } id && id != Guid.Empty ? LocationId.From(id) : (LocationId?)null;
        return (await workspace.Printers.ListAsync(workspace.CurrentTenant, location, cancellationToken))
            .Select(printer => new PrinterDto(
                printer.Id.Value, printer.LocationId.Value, printer.Name, printer.Protocol, printer.PrintsCustomerTickets, printer.Status,
                printer.TokenHint, printer.LastSeenAtUtc,
                printer.LastSeenAtUtc is { } seen && clock.UtcNow - seen < OnlineWindow + Printer.SeenResolution, printer.CreatedAtUtc,
                printer.PrintsKitchenTickets, printer.KitchenStationId))
            .ToList();
    }
}

/// <summary>Les derniers tickets (100 au plus), du plus récent au plus ancien.</summary>
public sealed record ListPrintJobsQuery(Guid? LocationId = null, int Take = 50) : IQuery<IReadOnlyList<PrintJobDto>>;

public sealed class ListPrintJobsHandler(PrintingWorkspace workspace) : IQueryHandler<ListPrintJobsQuery, IReadOnlyList<PrintJobDto>>
{
    public async Task<IReadOnlyList<PrintJobDto>> HandleAsync(ListPrintJobsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var location = query.LocationId is { } id && id != Guid.Empty ? LocationId.From(id) : (LocationId?)null;
        return (await workspace.Jobs.ListRecentAsync(workspace.CurrentTenant, location, Math.Clamp(query.Take, 1, 100), cancellationToken))
            .Select(job => new PrintJobDto(
                job.Id.Value, job.PrinterId.Value, job.LocationId.Value, job.Kind, job.Reference, job.Status, job.Attempts, job.CreatedAtUtc, job.SentAtUtc,
                job.CompletedAtUtc, job.FailureCode))
            .ToList();
    }
}

using Printing.Application.Abstractions;
using Printing.Domain;

namespace Printing.Application;

/// <summary>Le module en mémoire : deux restaurants, leurs établissements, et l'imprimante de la requête.</summary>
public sealed class PrintingFixture
    : ITenantContext, IPrintingTenants, IPrintingLocations, IPrinterRepository, IPrinterTokenLookup, IPrintJobRepository, IPrinterClient, IPrintingUnitOfWork
{
    public PrintingFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        CurrentTenantId = TenantA;
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        Cotonou = LocationId.New();
        Paris = LocationId.New();
        Locations[(TenantA, Cotonou)] = "Cotonou";
        Locations[(TenantB, Paris)] = "Paris";
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public LocationId Cotonou { get; }

    public LocationId Paris { get; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    public Dictionary<(TenantId, LocationId), string> Locations { get; } = [];

    public List<Printer> Printers { get; } = [];

    public List<PrintJob> Jobs { get; } = [];

    public PrinterId? PrinterId { get; set; }

    public FakeClock Clock { get; } = new();

    public PrintingWorkspace Workspace => new(this, this, this, this, this, this);

    public Commands.CreatePrinterHandler Create => new(Workspace, Clock);

    public Commands.RotatePrinterTokenHandler Rotate => new(Workspace, Clock);

    public Commands.SetPrinterDisabledHandler SetDisabled => new(Workspace, Clock);

    public Commands.PrintTestPageHandler TestPage => new(Workspace, Clock);

    public Commands.ReprintJobHandler Reprint => new(Workspace, Clock);

    public Commands.PrintCustomerTicketHandler CustomerTicket => new(Workspace, Clock);

    public Commands.AuthenticatePrinterHandler Authenticate => new(this, this);

    public Commands.PeekPrintJobHandler Peek => new(Workspace, this, Clock);

    public Commands.FetchPrintJobHandler Fetch => new(Workspace, this, Clock);

    public Commands.CompletePrintJobHandler Complete => new(Workspace, this, Clock);

    public Queries.ListPrintersHandler ListPrinters => new(Workspace, Clock);

    public Queries.ListPrintJobsHandler ListJobs => new(Workspace);

    /// <summary>Joue une requête d'imprimante : le tenant et l'imprimante viennent du jeton.</summary>
    public async Task<Commands.PrinterPrincipal?> SignInAsync(string token)
    {
        var principal = await Authenticate.HandleAsync(new Commands.AuthenticatePrinterQuery(token));
        CurrentTenantId = principal is null ? null : TenantId.From(principal.TenantId);
        PrinterId = principal is null ? null : Domain.PrinterId.From(principal.PrinterId);
        return principal;
    }

    Task<bool> IPrintingTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.GetValueOrDefault(tenantId));

    Task<PrintingLocation?> IPrintingLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult(Locations.TryGetValue((tenantId, locationId), out var name) ? new PrintingLocation(locationId, name) : null);

    Task<Printer?> IPrinterRepository.FindAsync(TenantId tenantId, PrinterId id, CancellationToken cancellationToken) =>
        Task.FromResult(Printers.SingleOrDefault(printer => printer.TenantId == tenantId && printer.Id == id));

    Task<IReadOnlyList<Printer>> IPrinterRepository.ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Printer>>(Printers.Where(printer => printer.TenantId == tenantId && (locationId is null || printer.LocationId == locationId)).ToList());

    void IPrinterRepository.Add(Printer printer) => Printers.Add(printer);

    Task<Printer?> IPrinterTokenLookup.FindByTokenHashAsync(TokenHash tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Printers.SingleOrDefault(printer => printer.TokenHash == tokenHash));

    Task<PrintJob?> IPrintJobRepository.FindAsync(TenantId tenantId, PrintJobId id, CancellationToken cancellationToken) =>
        Task.FromResult(Jobs.SingleOrDefault(job => job.TenantId == tenantId && job.Id == id));

    Task<PrintJob?> IPrintJobRepository.FindByReferenceAsync(TenantId tenantId, PrinterId printerId, PrintJobKind kind, string reference, CancellationToken cancellationToken) =>
        Task.FromResult(Jobs.SingleOrDefault(job => job.TenantId == tenantId && job.PrinterId == printerId && job.Kind == kind && job.Reference == reference));

    Task<IReadOnlyList<PrintJob>> IPrintJobRepository.ListOpenAsync(TenantId tenantId, PrinterId printerId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJob>>(Jobs
            .Where(job => job.TenantId == tenantId && job.PrinterId == printerId && job.Status is PrintJobStatus.Pending or PrintJobStatus.Sent)
            .OrderBy(job => job.CreatedAtUtc).Take(take).ToList());

    Task<IReadOnlyList<PrintJob>> IPrintJobRepository.ListRecentAsync(TenantId tenantId, LocationId? locationId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrintJob>>(Jobs
            .Where(job => job.TenantId == tenantId && (locationId is null || job.LocationId == locationId))
            .OrderByDescending(job => job.CreatedAtUtc).Take(take).ToList());

    void IPrintJobRepository.Add(PrintJob job) => Jobs.Add(job);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

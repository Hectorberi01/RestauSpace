using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Printing.Application;
using Printing.Application.Abstractions;
using Printing.Domain;

namespace Printing.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte. La seule lecture sans filtre de tenant
// est l'authentification d'une imprimante par son jeton (PrinterTokenLookup), et elle le déclare.

internal sealed class PrinterRepository(PrintingDbContext context) : IPrinterRepository
{
    public Task<Printer?> FindAsync(TenantId tenantId, PrinterId id, CancellationToken cancellationToken = default) =>
        context.Printers.SingleOrDefaultAsync(printer => printer.TenantId == tenantId && printer.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Printer>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default) =>
        await context.Printers
            .Where(printer => printer.TenantId == tenantId && (locationId == null || printer.LocationId == locationId))
            .OrderBy(printer => printer.Name)
            .ToListAsync(cancellationToken);

    public void Add(Printer printer) => context.Printers.Add(printer);
}

/// <summary>Tous tenants (l'imprimante n'a pas encore de tenant), lecture seule, par l'empreinte unique du jeton.</summary>
internal sealed class PrinterTokenLookup(PrintingDbContext context) : IPrinterTokenLookup
{
    public Task<Printer?> FindByTokenHashAsync(TokenHash tokenHash, CancellationToken cancellationToken = default) =>
        context.Printers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(printer => printer.TokenHash == tokenHash, cancellationToken);
}

internal sealed class PrintJobRepository(PrintingDbContext context) : IPrintJobRepository
{
    public Task<PrintJob?> FindAsync(TenantId tenantId, PrintJobId id, CancellationToken cancellationToken = default) =>
        context.PrintJobs.SingleOrDefaultAsync(job => job.TenantId == tenantId && job.Id == id, cancellationToken);

    public Task<PrintJob?> FindByReferenceAsync(TenantId tenantId, PrinterId printerId, PrintJobKind kind, string reference, CancellationToken cancellationToken = default) =>
        context.PrintJobs.SingleOrDefaultAsync(
            job => job.TenantId == tenantId && job.PrinterId == printerId && job.Kind == kind && job.Reference == reference, cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListOpenAsync(TenantId tenantId, PrinterId printerId, int take, CancellationToken cancellationToken = default) =>
        await context.PrintJobs
            .Where(job => job.TenantId == tenantId && job.PrinterId == printerId && (job.Status == PrintJobStatus.Pending || job.Status == PrintJobStatus.Sent))
            .OrderBy(job => job.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrintJob>> ListRecentAsync(TenantId tenantId, LocationId? locationId, int take, CancellationToken cancellationToken = default) =>
        await context.PrintJobs.AsNoTracking()
            .Where(job => job.TenantId == tenantId && (locationId == null || job.LocationId == locationId))
            .OrderByDescending(job => job.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(PrintJob job) => context.PrintJobs.Add(job);
}

internal sealed class PrintingUnitOfWork(PrintingDbContext context) : OutboxUnitOfWork<PrintingDbContext>(context), IPrintingUnitOfWork
{
    protected override DomainError ConcurrentUpdate => PrintingApplicationErrors.ConcurrentUpdate;

    /// <summary>Deux jetons identiques (impossible en pratique) ou le même ticket ajouté deux fois en même temps.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PrinterConfiguration.TokenIndex or PrintJobConfiguration.ReferenceIndex,
        }
            ? new ConcurrencyException(PrintingApplicationErrors.ConcurrentUpdate)
            : null;
}

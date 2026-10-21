using Printing.Application.Abstractions;
using Printing.Domain;

namespace Printing.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Le tenant vient du contexte (session de la console, ou jeton de l'imprimante), jamais
/// d'une valeur de requête ; une écriture du restaurant exige un tenant qui les accepte.
/// </summary>
public sealed class PrintingWorkspace(
    ITenantContext tenantContext,
    IPrintingTenants tenants,
    IPrintingLocations locations,
    IPrinterRepository printers,
    IPrintJobRepository jobs,
    IPrintingUnitOfWork unitOfWork)
{
    public IPrinterRepository Printers => printers;

    public IPrintJobRepository Jobs => jobs;

    public IPrintingLocations Locations => locations;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(PrintingApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<Printer> LoadPrinterAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await printers.FindAsync(CurrentTenant, PrinterId.From(id), cancellationToken))
        ?? throw new NotFoundException(PrintingApplicationErrors.PrinterNotFound);

    public async Task<PrintingLocation> LoadLocationAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await locations.FindAsync(CurrentTenant, LocationId.From(id), cancellationToken))
        ?? throw new NotFoundException(PrintingApplicationErrors.LocationNotFound);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

public static class PrintingApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("printing.concurrent_update", "The resource was changed by someone else; reload it and try again.", DomainErrorKind.Conflict);

    public static readonly DomainError TenantNotWritable =
        new("printing.tenant.not_writable", "The restaurant account is suspended or closed: ask the platform to restore it.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError PrinterNotFound = new("printing.printer.not_found", "The printer does not exist.");
    public static readonly DomainError JobNotFound = new("printing.job.not_found", "The print job does not exist.");
    public static readonly DomainError LocationNotFound = new("printing.location.not_found", "The location does not exist.");
    public static readonly DomainError PrinterRequired = new("printing.printer_required", "A printer token is required.");
}

/// <summary>Codes de permission déclarés par le module Printing (accordés via les rôles Staff).</summary>
public static class PrintingPermissions
{
    /// <summary>Voir les imprimantes et les derniers tickets.</summary>
    public const string View = "printing.view";

    /// <summary>Déclarer les imprimantes, leurs jetons, imprimer une page de test, réimprimer.</summary>
    public const string Manage = "printing.manage";

    public static IReadOnlyList<string> All { get; } = [View, Manage];
}

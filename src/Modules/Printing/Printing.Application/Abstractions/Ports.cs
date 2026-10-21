using Printing.Domain;

namespace Printing.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (console, ou jeton de
// l'imprimante), jamais d'une valeur de requête. Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IPrintingUnitOfWork : IUnitOfWork;

public interface IPrinterRepository
{
    Task<Printer?> FindAsync(TenantId tenantId, PrinterId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom ; <paramref name="locationId"/> null = tous les établissements.</summary>
    Task<IReadOnlyList<Printer>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default);

    void Add(Printer printer);
}

/// <summary>
/// Retrouve une imprimante par l'empreinte de son jeton, TOUS tenants confondus : c'est ainsi qu'une imprimante apprend
/// son tenant. Lecture seule.
/// </summary>
public interface IPrinterTokenLookup
{
    Task<Printer?> FindByTokenHashAsync(TokenHash tokenHash, CancellationToken cancellationToken = default);
}

public interface IPrintJobRepository
{
    Task<PrintJob?> FindAsync(TenantId tenantId, PrintJobId id, CancellationToken cancellationToken = default);

    Task<PrintJob?> FindByReferenceAsync(TenantId tenantId, PrinterId printerId, PrintJobKind kind, string reference, CancellationToken cancellationToken = default);

    /// <summary>Les tickets de l'imprimante encore à remettre (en file, ou remis sans accusé), du plus ancien au plus récent.</summary>
    Task<IReadOnlyList<PrintJob>> ListOpenAsync(TenantId tenantId, PrinterId printerId, int take, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<PrintJob>> ListRecentAsync(TenantId tenantId, LocationId? locationId, int take, CancellationToken cancellationToken = default);

    void Add(PrintJob job);
}

/// <summary>Ce que l'impression doit savoir du restaurant, fourni par l'hôte depuis Tenants.</summary>
public interface IPrintingTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis Locations ; null s'il n'existe pas.</summary>
public sealed record PrintingLocation(LocationId LocationId, string Name);

public interface IPrintingLocations
{
    Task<PrintingLocation?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>L'imprimante qui fait la requête (jeton authentifié) ; null hors d'une requête d'imprimante.</summary>
public interface IPrinterClient
{
    PrinterId? PrinterId { get; }
}

using Fiscal.Domain;

namespace Fiscal.Application;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité, jamais d'une
// valeur de requête. Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IFiscalUnitOfWork : IUnitOfWork;

public interface IFiscalProfileRepository
{
    Task<FiscalProfile?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(FiscalProfile profile);
}

public interface IFiscalInvoiceRepository
{
    Task<FiscalInvoice?> FindAsync(TenantId tenantId, FiscalInvoiceId id, CancellationToken cancellationToken = default);

    Task<FiscalInvoice?> FindBySourceAsync(TenantId tenantId, FiscalInvoiceKind kind, Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<FiscalInvoice>> ListAsync(
        TenantId tenantId, FiscalInvoiceStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int take, CancellationToken cancellationToken = default);

    void Add(FiscalInvoice invoice);
}

public sealed record DueFiscalInvoice(TenantId TenantId, FiscalInvoiceId Id);

/// <summary>Les factures à traiter maintenant, TOUS restaurants confondus (file de l'Infrastructure). Lecture seule.</summary>
public interface IDueFiscalInvoices
{
    Task<IReadOnlyList<DueFiscalInvoice>> ListAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coffre du jeton e-MECeF de chaque restaurant (chiffré par l'Infrastructure). Sa valeur ne doit JAMAIS apparaître dans un
/// log, un événement, une exception, une trace ni une réponse.
/// </summary>
public interface IFiscalTokenStore
{
    Task StoreAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default);

    /// <exception cref="InvalidOperationException">Jeton illisible (altéré, ou clé du coffre changée).</exception>
    Task<string?> ReadAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Ce que la fiscalité doit savoir du restaurant, fourni par l'hôte depuis Tenants.</summary>
public interface IFiscalTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Code de la devise du restaurant ; null s'il est inconnu.</summary>
    Task<string?> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <param name="TotalWithTax">Montant TTC de la ligne, remise déduite.</param>
public sealed record FiscalOrderLine(string Name, decimal Quantity, decimal TotalWithTax, decimal TaxRate);

/// <param name="Method">Moyen de paiement (nom stable : Cash, CardOnSite, MobileMoney...).</param>
/// <param name="Amount">Montant réglé (encaissé moins remboursé).</param>
public sealed record FiscalOrderPayment(string Method, decimal Amount);

public sealed record FiscalOrder(
    Guid OrderId,
    Guid LocationId,
    string Number,
    string CurrencyCode,
    decimal Total,
    IReadOnlyList<FiscalOrderLine> Lines,
    decimal? DeliveryFee,
    IReadOnlyList<FiscalOrderPayment> Payments);

/// <summary>Une commande du tenant courant, fournie par l'hôte depuis Orders et Payments ; null si elle n'existe pas.</summary>
public interface IFiscalOrders
{
    Task<FiscalOrder?> FindAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken = default);
}

public sealed record EmecfItem(string Name, long Price, decimal Quantity, string TaxGroup);

public sealed record EmecfPayment(string Name, long Amount);

/// <param name="Type">FV (vente) ou FA (avoir).</param>
/// <param name="Aib">A, B ou null.</param>
/// <param name="Reference">Pour un avoir : code MECeF/DGI de la facture d'origine, sans tirets (24 caractères).</param>
public sealed record EmecfInvoiceRequest(
    string Ifu, string Type, string? Aib, IReadOnlyList<EmecfItem> Items, IReadOnlyList<EmecfPayment> Payments, string OperatorName, string? Reference);

public sealed record EmecfStatus(string? Ifu, string? Nim, DateTimeOffset? TokenValidUntil);

/// <summary>
/// L'API e-MECeF de la DGI du Bénin : envoi d'une facture (la DGI calcule et la met en attente), puis confirmation (code
/// MECeF, QR code, compteurs). Le jeton est passé à chaque appel et n'est jamais conservé ni journalisé.
/// </summary>
public interface IFiscalAuthority
{
    /// <exception cref="FiscalAuthorityException">Jeton refusé, DGI injoignable ou réponse inattendue.</exception>
    Task<EmecfStatus> StatusAsync(FiscalMode mode, string token, CancellationToken cancellationToken = default);

    /// <summary>Renvoie l'uid de la facture en attente chez la DGI.</summary>
    /// <exception cref="FiscalAuthorityException">Refus (code DGI) ou DGI injoignable.</exception>
    Task<string> SubmitAsync(FiscalMode mode, string token, EmecfInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <exception cref="FiscalAuthorityException">Refus (code DGI) ou DGI injoignable.</exception>
    Task<FiscalSecurity> ConfirmAsync(FiscalMode mode, string token, string uid, CancellationToken cancellationToken = default);
}

/// <summary>
/// Échec d'un appel à la DGI. <see cref="Permanent"/> : la facture est refusée telle quelle (inutile de réessayer). Le
/// message ne contient jamais le jeton ni le contenu de la facture.
/// </summary>
public sealed class FiscalAuthorityException : Exception
{
    public FiscalAuthorityException()
    {
        Code = "unknown";
    }

    public FiscalAuthorityException(string message)
        : base(message)
    {
        Code = "unknown";
    }

    public FiscalAuthorityException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "unknown";
    }

    public FiscalAuthorityException(string code, string message, bool permanent, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Permanent = permanent;
    }

    public string Code { get; }

    public bool Permanent { get; }
}

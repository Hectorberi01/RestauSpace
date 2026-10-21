namespace Fiscal.Domain;

public readonly record struct FiscalInvoiceId
{
    private FiscalInvoiceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FiscalInvoiceId New() => new(Guid.CreateVersion7());

    public static FiscalInvoiceId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new FiscalInvoiceId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum FiscalInvoiceKind
{
    /// <summary>Facture de vente (FV).</summary>
    Sale = 1,

    /// <summary>Facture d'avoir (FA), liée à la facture de vente d'origine.</summary>
    CreditNote = 2,
}

public enum FiscalInvoiceStatus
{
    /// <summary>À envoyer à la DGI (première fois ou nouvel essai programmé).</summary>
    Pending = 1,

    /// <summary>Acceptée par la DGI, en attente de confirmation (2 minutes au plus chez la DGI).</summary>
    Submitted = 2,

    /// <summary>Confirmée : code MECeF et QR code reçus. Terminal.</summary>
    Certified = 3,

    /// <summary>Refusée par la DGI ou abandonnée après trop d'essais. Relançable à la main.</summary>
    Failed = 4,
}

/// <param name="UnitPrice">Prix unitaire TTC en francs CFA (entier).</param>
/// <param name="TaxGroup">Groupe e-MECeF, A à F.</param>
public sealed record FiscalLine(string Name, long UnitPrice, decimal Quantity, string TaxGroup)
{
    public const int MaxNameLength = 120;
}

/// <param name="Type">Type de paiement e-MECeF : ESPECES, CARTEBANCAIRE, MOBILEMONEY, VIREMENT, CHEQUES, CREDIT, AUTRE.</param>
public sealed record FiscalPayment(string Type, long Amount)
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "ESPECES", "CARTEBANCAIRE", "MOBILEMONEY", "VIREMENT", "CHEQUES", "CREDIT", "AUTRE",
    };
}

/// <summary>Éléments de sécurité rendus par la DGI à la confirmation : ils s'impriment sur le reçu.</summary>
public sealed record FiscalSecurity(string CodeMecef, string QrCode, string Counters, string Nim, string DateTime);

/// <summary>
/// Facture normalisée d'une vente ou d'un remboursement : un instantané figé (lignes, paiements, total en francs CFA)
/// envoyé à la DGI par la file de l'Infrastructure, avec nouvel essai espacé tant que la DGI ne répond pas. Une seule
/// facture par source (commande pour une vente, remboursement pour un avoir). Aucune donnée du client.
/// </summary>
public sealed class FiscalInvoice : AggregateRoot<FiscalInvoiceId>, ITenantOwned
{
    public const int MaxLines = 200;
    public const int MaxAttempts = 12;
    public const int MaxErrorLength = 200;

    /// <summary>Une facture acceptée mais non confirmée expire chez la DGI : passé ce délai, on la renvoie.</summary>
    public static readonly TimeSpan SubmissionLifetime = TimeSpan.FromSeconds(90);

    private List<FiscalLine> _lines = [];
    private List<FiscalPayment> _payments = [];

    private FiscalInvoice(
        FiscalInvoiceId id, TenantId tenantId, Guid orderId, Guid locationId, FiscalInvoiceKind kind, Guid sourceId, string orderNumber,
        FiscalMode mode, long total, FiscalInvoiceId? originalInvoiceId, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        LocationId = locationId;
        Kind = kind;
        SourceId = sourceId;
        OrderNumber = orderNumber;
        Mode = mode;
        Total = total;
        OriginalInvoiceId = originalInvoiceId;
        Status = FiscalInvoiceStatus.Pending;
        NextAttemptAtUtc = now;
        CreatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private FiscalInvoice()
    {
        OrderNumber = null!;
    }

    public TenantId TenantId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid LocationId { get; private set; }

    public FiscalInvoiceKind Kind { get; private set; }

    /// <summary>La commande (vente) ou le remboursement (avoir) qui a produit la facture : unique par type.</summary>
    public Guid SourceId { get; private set; }

    public string OrderNumber { get; private set; }

    /// <summary>Environnement DGI au moment de la demande (bac à sable ou production).</summary>
    public FiscalMode Mode { get; private set; }

    /// <summary>Total TTC en francs CFA.</summary>
    public long Total { get; private set; }

    /// <summary>Pour un avoir : la facture de vente d'origine.</summary>
    public FiscalInvoiceId? OriginalInvoiceId { get; private set; }

    public IReadOnlyList<FiscalLine> Lines => _lines.AsReadOnly();

    public IReadOnlyList<FiscalPayment> Payments => _payments.AsReadOnly();

    public FiscalInvoiceStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    /// <summary>Identifiant de la demande chez la DGI (entre l'envoi et la confirmation).</summary>
    public string? Uid { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public FiscalSecurity? Security { get; private set; }

    /// <summary>Code d'erreur court (DGI ou transport), jamais de contenu de la facture.</summary>
    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CertifiedAtUtc { get; private set; }

    public static FiscalInvoice CreateSale(
        FiscalProfile profile, Guid orderId, Guid locationId, string? orderNumber, IReadOnlyList<FiscalLine> lines, IReadOnlyList<FiscalPayment> payments, IClock clock) =>
        Create(profile, FiscalInvoiceKind.Sale, orderId, orderId, locationId, orderNumber, lines, payments, null, clock);

    /// <param name="original">Facture de vente de la commande remboursée (même restaurant).</param>
    public static FiscalInvoice CreateCreditNote(
        FiscalProfile profile, FiscalInvoice original, Guid refundId, IReadOnlyList<FiscalLine> lines, IReadOnlyList<FiscalPayment> payments, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(original);
        DomainException.ThrowIf(original.Kind != FiscalInvoiceKind.Sale || original.TenantId != profile?.TenantId, FiscalErrors.OriginalInvalid);
        var total = lines?.Sum(line => (long)Math.Round(line.UnitPrice * line.Quantity, MidpointRounding.AwayFromZero)) ?? 0;
        DomainException.ThrowIf(total > original.Total, FiscalErrors.CreditExceedsOriginal);
        return Create(profile!, FiscalInvoiceKind.CreditNote, original.OrderId, refundId, original.LocationId, original.OrderNumber, lines!, payments, original.Id, clock);
    }

    private static FiscalInvoice Create(
        FiscalProfile profile, FiscalInvoiceKind kind, Guid orderId, Guid sourceId, Guid locationId, string? orderNumber,
        IReadOnlyList<FiscalLine> lines, IReadOnlyList<FiscalPayment> payments, FiscalInvoiceId? original, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!profile.IsActive, FiscalErrors.Disabled);
        DomainException.ThrowIf(orderId == Guid.Empty || sourceId == Guid.Empty || locationId == Guid.Empty, CommonErrors.EmptyId);
        var number = orderNumber?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(number) || number.Length > 40, FiscalErrors.LinesInvalid);
        DomainException.ThrowIf(lines.Count is 0 or > MaxLines, FiscalErrors.LinesInvalid);
        DomainException.ThrowIf(
            lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.Name) || line.Name.Length > FiscalLine.MaxNameLength || line.Name.Any(char.IsControl)
                || line.UnitPrice <= 0 || line.Quantity <= 0 || line.TaxGroup is not ("A" or "B" or "C" or "D" or "E" or "F")),
            FiscalErrors.LinesInvalid);
        DomainException.ThrowIf(payments.Count == 0 || payments.Any(payment => payment is null || payment.Amount <= 0 || !FiscalPayment.Types.Contains(payment.Type)), FiscalErrors.PaymentsInvalid);
        var total = lines.Sum(line => (long)Math.Round(line.UnitPrice * line.Quantity, MidpointRounding.AwayFromZero));
        DomainException.ThrowIf(payments.Sum(payment => payment.Amount) != total, FiscalErrors.PaymentsMismatch);

        var invoice = new FiscalInvoice(FiscalInvoiceId.New(), profile.TenantId, orderId, locationId, kind, sourceId, number!, profile.Mode, total, original, clock.UtcNow)
        {
            _lines = lines.ToList(),
            _payments = payments.ToList(),
        };
        invoice.Raise(new FiscalInvoiceRequested(profile.TenantId, invoice.Id, kind, orderId, clock.UtcNow));
        return invoice;
    }

    /// <summary>À traiter maintenant : en attente d'envoi ou de confirmation, et son heure est venue.</summary>
    public bool IsDue(DateTimeOffset nowUtc) =>
        Status is FiscalInvoiceStatus.Pending or FiscalInvoiceStatus.Submitted && NextAttemptAtUtc is { } next && next <= nowUtc;

    /// <summary>La demande acceptée par la DGI a-t-elle expiré avant sa confirmation ?</summary>
    public bool SubmissionExpired(DateTimeOffset nowUtc) =>
        Status == FiscalInvoiceStatus.Submitted && SubmittedAtUtc is { } submitted && nowUtc - submitted > SubmissionLifetime;

    public void MarkSubmitted(string? uid, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != FiscalInvoiceStatus.Pending, FiscalErrors.NotPending);
        var value = uid?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(value) || value.Length > 64, FiscalErrors.AuthorityResponseInvalid);
        Uid = value;
        SubmittedAtUtc = clock.UtcNow;
        Status = FiscalInvoiceStatus.Submitted;
        NextAttemptAtUtc = clock.UtcNow;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>Demande expirée chez la DGI : elle sera renvoyée entière.</summary>
    public void Resubmit(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != FiscalInvoiceStatus.Submitted, FiscalErrors.NotSubmitted);
        Uid = null;
        SubmittedAtUtc = null;
        Status = FiscalInvoiceStatus.Pending;
        NextAttemptAtUtc = clock.UtcNow;
    }

    public void Certify(FiscalSecurity security, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != FiscalInvoiceStatus.Submitted, FiscalErrors.NotSubmitted);
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(security.CodeMecef) || string.IsNullOrWhiteSpace(security.QrCode) || security.QrCode.Length > 500,
            FiscalErrors.AuthorityResponseInvalid);
        Security = security;
        Status = FiscalInvoiceStatus.Certified;
        CertifiedAtUtc = clock.UtcNow;
        NextAttemptAtUtc = null;
        ErrorCode = null;
        ErrorMessage = null;
        Raise(new FiscalInvoiceCertified(TenantId, Id, Kind, OrderId, clock.UtcNow));
    }

    /// <summary>
    /// Échec d'un essai. Passager (DGI injoignable, trop de demandes en attente) : nouvel essai espacé, abandon après
    /// <see cref="MaxAttempts"/>. Définitif (facture refusée) : échec aussitôt.
    /// </summary>
    public void RecordFailure(string? code, string? message, bool permanent, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status is FiscalInvoiceStatus.Certified or FiscalInvoiceStatus.Failed, FiscalErrors.NotInProgress);
        Attempts++;
        ErrorCode = Clean(code, 40) ?? "unknown";
        ErrorMessage = Clean(message, MaxErrorLength);
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = FiscalInvoiceStatus.Failed;
            NextAttemptAtUtc = null;
            Raise(new FiscalInvoiceFailed(TenantId, Id, Kind, OrderId, ErrorCode, clock.UtcNow));
            return;
        }

        // 1, 2, 4, 8... minutes, une heure au plus.
        NextAttemptAtUtc = clock.UtcNow + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Attempts - 1)));
    }

    /// <summary>Relance à la main d'une facture en échec (après correction du profil ou du jeton).</summary>
    public void Retry(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != FiscalInvoiceStatus.Failed, FiscalErrors.NotFailed);
        Status = FiscalInvoiceStatus.Pending;
        Attempts = 0;
        Uid = null;
        SubmittedAtUtc = null;
        NextAttemptAtUtc = clock.UtcNow;
    }

    private static string? Clean(string? text, int length)
    {
        var value = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return value.Length == 0 ? null : value.Length > length ? value[..length] : value;
    }
}

public sealed record FiscalInvoiceRequested(TenantId TenantId, FiscalInvoiceId InvoiceId, FiscalInvoiceKind Kind, Guid OrderId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record FiscalInvoiceCertified(TenantId TenantId, FiscalInvoiceId InvoiceId, FiscalInvoiceKind Kind, Guid OrderId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record FiscalInvoiceFailed(TenantId TenantId, FiscalInvoiceId InvoiceId, FiscalInvoiceKind Kind, Guid OrderId, string ErrorCode, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public static class FiscalErrors
{
    public static readonly DomainError IfuInvalid = new("fiscal.profile.ifu_invalid", "The IFU is made of 13 digits.");
    public static readonly DomainError RccmInvalid = new("fiscal.profile.rccm_invalid", "The trade register number is limited to 40 characters.");
    public static readonly DomainError RegimeInvalid = new("fiscal.profile.regime_invalid", "Unknown tax regime.");
    public static readonly DomainError AibInvalid = new("fiscal.profile.aib_invalid", "Unknown AIB rate.");
    public static readonly DomainError ModeInvalid = new("fiscal.profile.mode_invalid", "Unknown e-MECeF mode.");
    public static readonly DomainError TokenMissing = new("fiscal.profile.token_missing", "Enter the e-MECeF token before enabling standardized invoices.");
    public static readonly DomainError Disabled = new("fiscal.profile.disabled", "Standardized invoices are disabled for this restaurant.");
    public static readonly DomainError LinesInvalid = new("fiscal.invoice.lines_invalid", "An invoice needs 1 to 200 lines, each with a name, a positive price and quantity, and a tax group.");
    public static readonly DomainError PaymentsInvalid = new("fiscal.invoice.payments_invalid", "An invoice needs positive payments of a known e-MECeF type.");
    public static readonly DomainError PaymentsMismatch = new("fiscal.invoice.payments_mismatch", "The payments must add up to the invoice total.");
    public static readonly DomainError OriginalInvalid = new("fiscal.invoice.original_invalid", "A credit note refers to a sale invoice of the same restaurant.");
    public static readonly DomainError CreditExceedsOriginal = new("fiscal.invoice.credit_exceeds_original", "A credit note cannot exceed its original invoice.");
    public static readonly DomainError NotPending = new("fiscal.invoice.not_pending", "The invoice is not waiting to be sent.", DomainErrorKind.Conflict);
    public static readonly DomainError NotSubmitted = new("fiscal.invoice.not_submitted", "The invoice is not waiting for confirmation.", DomainErrorKind.Conflict);
    public static readonly DomainError NotInProgress = new("fiscal.invoice.not_in_progress", "The invoice is already certified or failed.", DomainErrorKind.Conflict);
    public static readonly DomainError NotFailed = new("fiscal.invoice.not_failed", "Only a failed invoice can be retried.", DomainErrorKind.Conflict);
    public static readonly DomainError AuthorityResponseInvalid = new("fiscal.invoice.authority_response_invalid", "The tax authority answer is incomplete.");
}

namespace Printing.Domain;

public readonly record struct PrintJobId
{
    private PrintJobId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PrintJobId New() => new(Guid.CreateVersion7());

    public static PrintJobId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PrintJobId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum PrintJobKind
{
    CustomerTicket = 1,
    TestPage = 2,

    /// <summary>Bon de cuisine d'une commande acceptée (sans prix ni donnée du client).</summary>
    KitchenTicket = 3,

    /// <summary>Reçu d'une commande, demandé depuis la console.</summary>
    OrderReceipt = 4,
}

public enum PrintJobStatus
{
    /// <summary>En file, pas encore remis à l'imprimante.</summary>
    Pending = 1,

    /// <summary>Remis à l'imprimante (ou au relais), en attente de son accusé.</summary>
    Sent = 2,
    Printed = 3,
    Failed = 4,

    /// <summary>Jamais remis dans le délai : abandonné (l'imprimante était éteinte).</summary>
    Expired = 5,
}

/// <summary>
/// Un ticket à imprimer sur une imprimante : sa file, ses remises et son accusé. Un ticket remis sans accusé est remis
/// à nouveau après <see cref="ResendAfter"/> (imprimante redémarrée) ; un ticket jamais remis expire après
/// <see cref="ExpireAfter"/>. <see cref="Reference"/> rend l'ajout idempotent (une commande = un ticket).
/// </summary>
public sealed class PrintJob : AggregateRoot<PrintJobId>, ITenantOwned
{
    public const int MaxReferenceLength = 80;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromHours(2);

    private PrintJob(PrintJobId id, TenantId tenantId, PrinterId printerId, LocationId locationId, PrintJobKind kind, string reference, TicketDocument document, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        PrinterId = printerId;
        LocationId = locationId;
        Kind = kind;
        Reference = reference;
        Document = document;
        Status = PrintJobStatus.Pending;
        CreatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private PrintJob()
    {
        Reference = null!;
        Document = null!;
    }

    public TenantId TenantId { get; private set; }

    public PrinterId PrinterId { get; private set; }

    public LocationId LocationId { get; private set; }

    public PrintJobKind Kind { get; private set; }

    public string Reference { get; private set; }

    public TicketDocument Document { get; private set; }

    public PrintJobStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Code renvoyé par l'imprimante ou le relais en cas d'échec (jamais de contenu du ticket).</summary>
    public string? FailureCode { get; private set; }

    public static PrintJob Create(Printer printer, PrintJobKind kind, string? reference, TicketDocument document, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!printer.IsActive, PrintJobErrors.PrinterDisabled);
        DomainException.ThrowIf(!Enum.IsDefined(kind), PrintJobErrors.KindInvalid);
        var trimmed = reference?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxReferenceLength, PrintJobErrors.ReferenceInvalid);
        return new PrintJob(PrintJobId.New(), printer.TenantId, printer.Id, printer.LocationId, kind, trimmed!, document, clock.UtcNow);
    }

    /// <summary>Peut être remis maintenant : en file, ou remis sans accusé depuis trop longtemps (et pas trop souvent).</summary>
    public bool IsDue(DateTimeOffset nowUtc) =>
        Status == PrintJobStatus.Pending
        || (Status == PrintJobStatus.Sent && SentAtUtc is { } sent && nowUtc - sent >= ResendAfter && Attempts < MaxAttempts);

    public void MarkSent(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsDue(clock.UtcNow), PrintJobErrors.NotDue);
        Status = PrintJobStatus.Sent;
        SentAtUtc = clock.UtcNow;
        Attempts++;
    }

    /// <summary>Accusé de l'imprimante : imprimé, ou échec avec son code. Rejouable pour le même résultat.</summary>
    public void Complete(bool printed, string? failureCode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status is PrintJobStatus.Printed && printed)
        {
            return;
        }

        DomainException.ThrowIf(Status != PrintJobStatus.Sent, PrintJobErrors.NotSent);
        Status = printed ? PrintJobStatus.Printed : PrintJobStatus.Failed;
        FailureCode = printed ? null : Truncate(failureCode);
        CompletedAtUtc = clock.UtcNow;
    }

    /// <summary>Jamais remis (ou jamais acquitté) dans le délai : abandonné.</summary>
    public bool ExpireIfStale(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status is not (PrintJobStatus.Pending or PrintJobStatus.Sent) || clock.UtcNow - CreatedAtUtc < ExpireAfter)
        {
            return false;
        }

        Status = PrintJobStatus.Expired;
        CompletedAtUtc = clock.UtcNow;
        return true;
    }

    private static string? Truncate(string? code)
    {
        var trimmed = code?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : new string(trimmed.Where(c => !char.IsControl(c)).Take(60).ToArray());
    }
}

public static class PrintJobErrors
{
    public static readonly DomainError DocumentInvalid = new("printing.job.document_invalid", "The ticket is empty, too long or contains unsupported characters.");
    public static readonly DomainError PrinterDisabled = new("printing.job.printer_disabled", "The printer is disabled.");
    public static readonly DomainError KindInvalid = new("printing.job.kind_invalid", "The print job kind is not recognized.");
    public static readonly DomainError ReferenceInvalid = new("printing.job.reference_invalid", "The print job reference is invalid.");
    public static readonly DomainError NotDue = new("printing.job.not_due", "The print job is not waiting to be printed.", DomainErrorKind.Conflict);
    public static readonly DomainError NotSent = new("printing.job.not_sent", "The print job has not been sent to the printer.", DomainErrorKind.Conflict);
}

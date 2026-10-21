namespace Printing.Domain;

public readonly record struct PrinterId
{
    private PrinterId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PrinterId New() => new(Guid.CreateVersion7());

    public static PrinterId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PrinterId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Comment l'imprimante reçoit ses tickets.</summary>
public enum PrinterProtocol
{
    /// <summary>Imprimante Star « cloud » : elle interroge elle-même l'API en HTTPS (Star CloudPRNT).</summary>
    StarCloudPrnt = 1,

    /// <summary>Imprimante ESC/POS du réseau local, alimentée par un relais (tablette ou boîtier) qui interroge l'API.</summary>
    EscPosRelay = 2,
}

public enum PrinterStatus
{
    Active = 1,
    Disabled = 2,
}

/// <summary>
/// Une imprimante déclarée dans un établissement. Elle s'authentifie par un jeton (seule son empreinte est gardée) placé
/// dans l'adresse qu'elle interroge. <see cref="PrintsCustomerTickets"/> : elle reçoit les tickets client des bornes.
/// </summary>
public sealed class Printer : AggregateRoot<PrinterId>, ITenantOwned
{
    public const int MaxNameLength = 80;
    public const int TokenHintLength = 4;

    /// <summary>Une imprimante qui interroge l'API plus souvent ne réécrit sa dernière présence qu'une fois par minute.</summary>
    public static readonly TimeSpan SeenResolution = TimeSpan.FromMinutes(1);

    private Printer(PrinterId id, TenantId tenantId, LocationId locationId, string name, PrinterProtocol protocol, TokenHash tokenHash, string tokenHint, bool printsCustomerTickets, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
        Protocol = protocol;
        TokenHash = tokenHash;
        TokenHint = tokenHint;
        PrintsCustomerTickets = printsCustomerTickets;
        Status = PrinterStatus.Active;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private Printer()
    {
        Name = null!;
        TokenHash = null!;
        TokenHint = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public string Name { get; private set; }

    public PrinterProtocol Protocol { get; private set; }

    public TokenHash TokenHash { get; private set; }

    public string TokenHint { get; private set; }

    public bool PrintsCustomerTickets { get; private set; }

    /// <summary>Elle reçoit les bons de cuisine de son établissement.</summary>
    public bool PrintsKitchenTickets { get; private set; }

    /// <summary>
    /// Poste de cuisine servi par l'imprimante (bar, grill...) : elle n'imprime que les lignes routées vers ce poste.
    /// Null : toutes les lignes du bon.
    /// </summary>
    public Guid? KitchenStationId { get; private set; }

    public PrinterStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Dernière fois que l'imprimante (ou son relais) a interrogé l'API ; null tant qu'elle ne l'a jamais fait.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; private set; }

    public bool IsActive => Status == PrinterStatus.Active;

    /// <param name="location">Un établissement du même restaurant.</param>
    public static Printer Create(
        TenantId tenantId, LocationId location, string? name, PrinterProtocol protocol, TokenHash tokenHash, string tokenHint, bool printsCustomerTickets, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(location);
        DomainException.ThrowIf(!Enum.IsDefined(protocol), PrinterErrors.ProtocolInvalid);
        DomainException.ThrowIf(tokenHint is not { Length: TokenHintLength }, PrinterErrors.TokenInvalid);
        return new Printer(PrinterId.New(), tenantId, location, NameOf(name), protocol, tokenHash, tokenHint, printsCustomerTickets, clock.UtcNow);
    }

    public void Update(string? name, bool printsCustomerTickets, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Name = NameOf(name);
        PrintsCustomerTickets = printsCustomerTickets;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Rôle en cuisine : imprimer les bons, éventuellement pour un seul poste.</summary>
    public void SetKitchenRole(bool printsKitchenTickets, Guid? kitchenStationId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(kitchenStationId == Guid.Empty, PrinterErrors.StationInvalid);
        DomainException.ThrowIf(!printsKitchenTickets && kitchenStationId is not null, PrinterErrors.StationInvalid);
        PrintsKitchenTickets = printsKitchenTickets;
        KitchenStationId = kitchenStationId;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Nouveau jeton : l'ancien cesse aussitôt de fonctionner (à reconfigurer dans l'imprimante ou le relais).</summary>
    public void RotateToken(TokenHash tokenHash, string tokenHint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(tokenHint is not { Length: TokenHintLength }, PrinterErrors.TokenInvalid);
        DomainException.ThrowIf(tokenHash == TokenHash, PrinterErrors.TokenInvalid);
        TokenHash = tokenHash;
        TokenHint = tokenHint;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsActive, PrinterErrors.AlreadyDisabled);
        Status = PrinterStatus.Disabled;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Enable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsActive, PrinterErrors.AlreadyActive);
        Status = PrinterStatus.Active;
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>L'imprimante vient d'interroger l'API ; vrai quand la présence a changé (à enregistrer).</summary>
    public bool MarkSeen(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (LastSeenAtUtc is { } seen && clock.UtcNow - seen < SeenResolution)
        {
            return false;
        }

        LastSeenAtUtc = clock.UtcNow;
        return true;
    }

    private static string NameOf(string? name)
    {
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), PrinterErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, PrinterErrors.NameTooLong);
        return trimmed;
    }
}

/// <summary>Empreinte SHA-256 (hexadécimal minuscule) d'un jeton d'appareil : le jeton lui-même n'est jamais gardé.</summary>
public sealed class TokenHash : ValueObject
{
    public const int Length = 64;

    private TokenHash(string value) => Value = value;

    public string Value { get; }

    public static TokenHash Create(string? value)
    {
        DomainException.ThrowIf(
            value is null || value.Length != Length || !value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')),
            PrinterErrors.TokenInvalid);
        return new TokenHash(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "TokenHash(***)";
}

public static class PrinterErrors
{
    public static readonly DomainError NameRequired = new("printing.printer.name_required", "The printer needs a name.");
    public static readonly DomainError NameTooLong = new("printing.printer.name_too_long", "The printer name is too long (80 characters at most).");
    public static readonly DomainError ProtocolInvalid = new("printing.printer.protocol_invalid", "The printer protocol is not recognized.");
    public static readonly DomainError TokenInvalid = new("printing.printer.token_invalid", "The printer token is invalid.");
    public static readonly DomainError AlreadyDisabled = new("printing.printer.already_disabled", "The printer is already disabled.", DomainErrorKind.Conflict);
    public static readonly DomainError StationInvalid = new("printing.printer.station_invalid", "A kitchen station is chosen only for a printer that prints kitchen tickets.");
    public static readonly DomainError AlreadyActive = new("printing.printer.already_active", "The printer is already active.", DomainErrorKind.Conflict);
}

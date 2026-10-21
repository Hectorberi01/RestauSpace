namespace Payments.Domain;

public readonly record struct CashSessionId
{
    private CashSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CashSessionId New() => new(Guid.CreateVersion7());

    public static CashSessionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CashSessionId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum CashSessionStatus
{
    Open = 1,
    Closed = 2,
}

public enum CashMovementKind
{
    /// <summary>Espèces ajoutées au tiroir hors vente (appoint de monnaie).</summary>
    PayIn = 1,

    /// <summary>Espèces retirées du tiroir hors remboursement (achat payé en caisse, dépôt en banque).</summary>
    PayOut = 2,
}

/// <param name="By">Utilisateur qui a fait le mouvement ; null hors requête authentifiée.</param>
public sealed record CashMovement(CashMovementKind Kind, Money Amount, string Reason, DateTimeOffset AtUtc, Guid? By)
{
    public const int MaxReasonLength = 200;
}

/// <summary>Une coupure comptée à la clôture : valeur faciale et nombre de billets ou de pièces.</summary>
public sealed record CashDenominationCount(decimal Value, int Count)
{
    public const int MaxCount = 100_000;
}

/// <summary>
/// Ligne du ticket Z pour un moyen de paiement. <see cref="Expected"/> : ce que la caisse devrait contenir d'après les
/// encaissements (et, pour les espèces, le fond et les mouvements) ; <see cref="Counted"/> : ce que le caissier a compté.
/// </summary>
public sealed record CashCountLine(PaymentMethodType Method, Money Expected, Money Counted)
{
    public Money Difference => Counted - Expected;
}

/// <summary>Encaissements nets d'un moyen pendant la session : captures moins remboursements réussis.</summary>
public sealed record CashSessionTakings(PaymentMethodType Method, Money Collected, Money Refunded)
{
    public Money Net => Collected - Refunded;
}

/// <summary>
/// Session de caisse d'un établissement : ouverte avec un fond, elle reçoit des entrées et sorties d'espèces, puis se
/// clôture par un comptage par moyen de paiement. La clôture fige l'attendu, le compté et l'écart (ticket Z) : rien ne
/// change ensuite. Une seule session ouverte à la fois par établissement (l'Application et un index unique y veillent).
/// Les encaissements ne sont pas portés par la session : ils sont lus à la clôture dans la période de la session.
/// </summary>
public sealed class CashSession : AggregateRoot<CashSessionId>, ITenantOwned
{
    public const int MaxMovements = 500;
    public const int MaxDenominations = 30;
    public const int MaxNoteLength = 500;

    private List<CashMovement> _movements = [];
    private List<CashCountLine> _lines = [];
    private List<CashDenominationCount> _denominations = [];

    private CashSession(
        CashSessionId id, TenantId tenantId, LocationId locationId, int number, Money openingFloat, DateTimeOffset openedAtUtc, Guid? openedBy)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Number = number;
        OpeningFloat = openingFloat;
        Status = CashSessionStatus.Open;
        OpenedAtUtc = openedAtUtc;
        OpenedBy = openedBy;
    }

    // Réservé à l'ORM.
    private CashSession()
    {
        OpeningFloat = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    /// <summary>Numéro de la session dans l'établissement (1, 2, 3...) : c'est le numéro du ticket Z.</summary>
    public int Number { get; private set; }

    public Currency Currency => OpeningFloat.Currency;

    /// <summary>Espèces mises dans le tiroir à l'ouverture (zéro possible).</summary>
    public Money OpeningFloat { get; private set; }

    public CashSessionStatus Status { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public Guid? OpenedBy { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public string? ClosingNote { get; private set; }

    public IReadOnlyList<CashMovement> Movements => _movements.AsReadOnly();

    /// <summary>Lignes du ticket Z ; vide tant que la session est ouverte.</summary>
    public IReadOnlyList<CashCountLine> Lines => _lines.AsReadOnly();

    /// <summary>Détail du comptage des espèces, s'il a été saisi coupure par coupure.</summary>
    public IReadOnlyList<CashDenominationCount> Denominations => _denominations.AsReadOnly();

    public Money PaidIn => Sum(CashMovementKind.PayIn);

    public Money PaidOut => Sum(CashMovementKind.PayOut);

    /// <summary>Somme des écarts (compté - attendu) de toutes les lignes ; zéro tant que la session est ouverte.</summary>
    public Money TotalDifference => _lines.Aggregate(Money.Zero(Currency), (sum, line) => sum + line.Difference);

    /// <param name="number">Numéro suivant dans l'établissement, attribué par l'Application.</param>
    public static CashSession Open(TenantId tenantId, LocationId locationId, int number, Money openingFloat, Guid? openedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(openingFloat);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(number < 1, CashSessionErrors.NumberInvalid);
        DomainException.ThrowIf(openingFloat.IsNegative, CashSessionErrors.OpeningFloatNegative);

        var session = new CashSession(CashSessionId.New(), tenantId, locationId, number, openingFloat, clock.UtcNow, openedBy);
        session.Raise(new CashSessionOpened(tenantId, session.Id, locationId, number, openingFloat, clock.UtcNow));
        return session;
    }

    public void RecordMovement(CashMovementKind kind, Money amount, string? reason, Guid? by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != CashSessionStatus.Open, CashSessionErrors.NotOpen);
        DomainException.ThrowIf(!Enum.IsDefined(kind), CashSessionErrors.MovementKindInvalid);
        DomainException.ThrowIf(amount.Currency != Currency, CashSessionErrors.CurrencyMismatch);
        DomainException.ThrowIf(!amount.IsPositive, CashSessionErrors.MovementAmountNotPositive);
        var text = reason?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(text), CashSessionErrors.MovementReasonRequired);
        DomainException.ThrowIf(text!.Length > CashMovement.MaxReasonLength, CashSessionErrors.MovementReasonTooLong);
        DomainException.ThrowIf(_movements.Count >= MaxMovements, CashSessionErrors.TooManyMovements);

        _movements.Add(new CashMovement(kind, amount, text, clock.UtcNow, by));
        Raise(new CashMovementRecorded(TenantId, Id, LocationId, kind, amount, clock.UtcNow));
    }

    /// <summary>
    /// Espèces attendues dans le tiroir pour des encaissements nets en espèces donnés : fond + ventes - remboursements +
    /// entrées - sorties.
    /// </summary>
    public Money ExpectedCash(Money netCashTakings)
    {
        ArgumentNullException.ThrowIfNull(netCashTakings);
        return OpeningFloat + netCashTakings + PaidIn - PaidOut;
    }

    /// <summary>
    /// Clôture : chaque moyen ayant des encaissements, et les espèces dans tous les cas, doit être compté. Si les
    /// coupures sont données, le compté des espèces est leur somme (et doit correspondre au montant saisi s'il l'est
    /// aussi). Un moyen compté sans encaissement est gardé, avec un attendu nul.
    /// </summary>
    /// <param name="takings">Encaissements de la période de la session, lus par l'Application (un par moyen au plus).</param>
    /// <param name="counted">Montant compté par moyen.</param>
    public void Close(
        IReadOnlyCollection<CashSessionTakings> takings,
        IReadOnlyDictionary<PaymentMethodType, decimal> counted,
        IReadOnlyCollection<CashDenominationCount>? denominations,
        string? note,
        Guid? by,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(takings);
        ArgumentNullException.ThrowIfNull(counted);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != CashSessionStatus.Open, CashSessionErrors.NotOpen);
        DomainException.ThrowIf(takings.GroupBy(item => item.Method).Any(group => group.Count() > 1), CashSessionErrors.TakingsDuplicate);
        DomainException.ThrowIf(
            takings.Any(item => item.Collected.Currency != Currency || item.Refunded.Currency != Currency), CashSessionErrors.CurrencyMismatch);
        DomainException.ThrowIf(counted.Keys.Any(method => !Enum.IsDefined(method)), CashSessionErrors.MethodInvalid);
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(text is { Length: > MaxNoteLength }, CashSessionErrors.NoteTooLong);

        var coins = (denominations ?? []).ToList();
        DomainException.ThrowIf(coins.Count > MaxDenominations, CashSessionErrors.TooManyDenominations);
        DomainException.ThrowIf(
            coins.Any(coin => coin.Value <= 0m || coin.Count < 0 || coin.Count > CashDenominationCount.MaxCount),
            CashSessionErrors.DenominationInvalid);
        DomainException.ThrowIf(coins.GroupBy(coin => coin.Value).Any(group => group.Count() > 1), CashSessionErrors.DenominationInvalid);

        var amounts = counted.ToDictionary(pair => pair.Key, pair => Count(pair.Value));
        if (coins.Count > 0)
        {
            var total = Count(coins.Sum(coin => coin.Value * coin.Count));
            DomainException.ThrowIf(
                amounts.TryGetValue(PaymentMethodType.Cash, out var declared) && declared != total, CashSessionErrors.DenominationsMismatch);
            amounts[PaymentMethodType.Cash] = total;
        }

        var net = takings.ToDictionary(item => item.Method, item => item.Net);
        var methods = net.Keys.Where(method => !net[method].IsZero).Append(PaymentMethodType.Cash).Distinct().ToList();
        DomainException.ThrowIf(methods.Any(method => !amounts.ContainsKey(method)), CashSessionErrors.CountMissing);

        _lines.Clear();
        foreach (var method in amounts.Keys.OrderBy(method => method == PaymentMethodType.Cash ? 0 : (int)method))
        {
            var takingsOfMethod = net.TryGetValue(method, out var value) ? value : Money.Zero(Currency);
            var expected = method == PaymentMethodType.Cash ? ExpectedCash(takingsOfMethod) : takingsOfMethod;
            _lines.Add(new CashCountLine(method, expected, amounts[method]));
        }

        _denominations.Clear();
        _denominations.AddRange(coins.Where(coin => coin.Count > 0).OrderByDescending(coin => coin.Value));
        ClosingNote = text;
        ClosedBy = by;
        ClosedAtUtc = clock.UtcNow;
        Status = CashSessionStatus.Closed;
        Raise(new CashSessionClosed(TenantId, Id, LocationId, Number, TotalDifference, clock.UtcNow));
    }

    private Money Count(decimal amount)
    {
        DomainException.ThrowIf(amount < 0m, CashSessionErrors.CountNegative);
        return Money.Of(amount, Currency);
    }

    private Money Sum(CashMovementKind kind) =>
        _movements.Where(movement => movement.Kind == kind).Aggregate(Money.Zero(Currency), (sum, movement) => sum + movement.Amount);
}

public static class CashSessionErrors
{
    public static readonly DomainError NumberInvalid = new("payments.cash_session.number_invalid", "A cash session number starts at 1.");
    public static readonly DomainError OpeningFloatNegative = new("payments.cash_session.opening_float_negative", "The opening float cannot be negative.");
    public static readonly DomainError NotOpen = new("payments.cash_session.not_open", "The cash session is closed: nothing can be added to it.");
    public static readonly DomainError CurrencyMismatch = new("payments.cash_session.currency_mismatch", "Every amount of a cash session uses its currency.");
    public static readonly DomainError MovementKindInvalid = new("payments.cash_session.movement_kind_invalid", "Unknown cash movement kind.");
    public static readonly DomainError MovementAmountNotPositive = new("payments.cash_session.movement_amount_not_positive", "A cash movement amount must be greater than zero.");
    public static readonly DomainError MovementReasonRequired = new("payments.cash_session.movement_reason_required", "Say why cash is added or removed.");
    public static readonly DomainError MovementReasonTooLong = new("payments.cash_session.movement_reason_too_long", "The reason of a cash movement is limited to 200 characters.");
    public static readonly DomainError TooManyMovements = new("payments.cash_session.too_many_movements", "A cash session holds at most 500 movements.");
    public static readonly DomainError TakingsDuplicate = new("payments.cash_session.takings_duplicate", "Takings are given once per payment method.");
    public static readonly DomainError MethodInvalid = new("payments.cash_session.method_invalid", "Unknown payment method.");
    public static readonly DomainError NoteTooLong = new("payments.cash_session.note_too_long", "The closing note is limited to 500 characters.");
    public static readonly DomainError TooManyDenominations = new("payments.cash_session.too_many_denominations", "At most 30 denominations can be counted.");
    public static readonly DomainError DenominationInvalid = new("payments.cash_session.denomination_invalid", "Each denomination is given once, with a positive value and a count between 0 and 100,000.");
    public static readonly DomainError DenominationsMismatch = new("payments.cash_session.denominations_mismatch", "The counted cash amount differs from the sum of the denominations.");
    public static readonly DomainError CountMissing = new("payments.cash_session.count_missing", "Count the cash and every payment method that has takings during the session.");
    public static readonly DomainError CountNegative = new("payments.cash_session.count_negative", "A counted amount cannot be negative.");
}

public sealed record CashSessionOpened(
    TenantId TenantId, CashSessionId CashSessionId, LocationId LocationId, int Number, Money OpeningFloat, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record CashMovementRecorded(
    TenantId TenantId, CashSessionId CashSessionId, LocationId LocationId, CashMovementKind Kind, Money Amount, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record CashSessionClosed(
    TenantId TenantId, CashSessionId CashSessionId, LocationId LocationId, int Number, Money TotalDifference, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

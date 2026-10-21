namespace Reservations.Domain;

/// <summary>Quand l'établissement demande un acompte.</summary>
public enum DepositMode
{
    Off = 0,

    /// <summary>À partir de <see cref="DepositRule.MinPartySize"/> couverts.</summary>
    FromPartySize = 1,

    Always = 2,
}

/// <summary>Le montant réglé s'entend par couvert ou pour toute la réservation.</summary>
public enum DepositBasis
{
    PerGuest = 1,
    PerReservation = 2,
}

/// <summary>
/// Règle d'acompte d'un établissement. Le montant est dans la devise du restaurant ; il est demandé à la création
/// d'une réservation qui remplit la règle et doit être réglé dans <see cref="PaymentWindow"/>, sinon la réservation est
/// annulée et ses places rendues. La règle ne s'applique pas aux réservations existantes.
/// </summary>
public sealed class DepositRule : ValueObject
{
    public const decimal MaxAmount = 10_000_000m;

    public static readonly TimeSpan DefaultPaymentWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinPaymentWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxPaymentWindow = TimeSpan.FromHours(48);

    private DepositRule(DepositMode mode, DepositBasis basis, Money? amount, int minPartySize, TimeSpan paymentWindow)
    {
        Mode = mode;
        Basis = basis;
        Amount = amount;
        MinPartySize = minPartySize;
        PaymentWindow = paymentWindow;
    }

    public static DepositRule Off { get; } = new(DepositMode.Off, DepositBasis.PerGuest, null, 1, DefaultPaymentWindow);

    public DepositMode Mode { get; }

    public DepositBasis Basis { get; }

    /// <summary>Null quand la règle est désactivée.</summary>
    public Money? Amount { get; }

    /// <summary>Taille de groupe à partir de laquelle l'acompte est demandé (mode <see cref="DepositMode.FromPartySize"/>).</summary>
    public int MinPartySize { get; }

    public TimeSpan PaymentWindow { get; }

    public bool IsEnabled => Mode != DepositMode.Off;

    public static DepositRule Create(DepositMode mode, DepositBasis basis, Money? amount, int minPartySize, TimeSpan paymentWindow)
    {
        DomainException.ThrowIf(!Enum.IsDefined(mode) || !Enum.IsDefined(basis), DepositErrors.RuleInvalid);
        if (mode == DepositMode.Off)
        {
            return Off;
        }

        DomainException.ThrowIf(amount is null || !amount.IsPositive || amount.Amount > MaxAmount, DepositErrors.AmountInvalid);
        DomainException.ThrowIf(minPartySize is < 1 or > ReservationPolicy.MaxPartySizeCeiling, DepositErrors.MinPartySizeInvalid);
        DomainException.ThrowIf(paymentWindow < MinPaymentWindow || paymentWindow > MaxPaymentWindow, DepositErrors.PaymentWindowInvalid);
        return new DepositRule(mode, basis, amount, mode == DepositMode.Always ? 1 : minPartySize, paymentWindow);
    }

    /// <summary>L'acompte demandé pour ce groupe ; null si la règle ne le demande pas.</summary>
    public Money? AmountFor(int partySize)
    {
        if (Amount is null || partySize < 1 || (Mode == DepositMode.FromPartySize && partySize < MinPartySize))
        {
            return null;
        }

        return Basis == DepositBasis.PerGuest ? Money.Of(Amount.Amount * partySize, Amount.Currency) : Amount;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Mode;
        yield return Basis;
        yield return Amount;
        yield return MinPartySize;
        yield return PaymentWindow;
    }
}

/// <summary>Où en est l'acompte d'une réservation.</summary>
public enum DepositStatus
{
    /// <summary>Demandé, pas encore réglé : la réservation attend.</summary>
    AwaitingPayment = 1,

    Paid = 2,

    /// <summary>Déduit de l'addition du repas.</summary>
    Applied = 3,

    /// <summary>Remboursement demandé au prestataire, pas encore confirmé.</summary>
    RefundRequested = 4,

    Refunded = 5,

    /// <summary>Gardé par le restaurant (no-show, annulation tardive par l'équipe).</summary>
    Forfeited = 6,

    /// <summary>L'équipe a renoncé à l'acompte.</summary>
    Waived = 7,
}

public static class DepositErrors
{
    public static readonly DomainError RuleInvalid = new("reservations.deposit.rule_invalid", "The deposit rule is invalid.");
    public static readonly DomainError AmountInvalid = new("reservations.deposit.amount_invalid", "The deposit amount must be positive and below 10,000,000.");
    public static readonly DomainError MinPartySizeInvalid = new("reservations.deposit.min_party_size_invalid", "The party size from which a deposit is asked is invalid.");
    public static readonly DomainError PaymentWindowInvalid = new("reservations.deposit.payment_window_invalid", "The time to pay the deposit must be between 5 minutes and 48 hours.");
    public static readonly DomainError AlreadyRequired = new("reservations.deposit.already_required", "A deposit is already asked for this reservation.");
    public static readonly DomainError NotRequired = new("reservations.deposit.not_required", "No deposit is asked for this reservation.");
    public static readonly DomainError NotAwaitingPayment = new("reservations.deposit.not_awaiting_payment", "The deposit is not awaiting payment.");
    public static readonly DomainError NotPaid = new("reservations.deposit.not_paid", "The deposit is not paid, or already deducted, refunded or kept.");
    public static readonly DomainError NotRefundable = new("reservations.deposit.not_refundable", "Only a paid or kept deposit can be refunded.");
    public static readonly DomainError DueBeforeInvalid = new("reservations.deposit.due_before_invalid", "The deposit must be payable in the future.");
    public static readonly DomainError CurrencyMismatch = new("reservations.deposit.currency_mismatch", "The amount is not in the deposit currency.");
    public static readonly DomainError NotDue = new("reservations.deposit.not_due", "The deposit is not yet overdue.");
}

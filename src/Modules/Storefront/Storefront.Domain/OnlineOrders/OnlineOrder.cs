namespace Storefront.Domain;

/// <summary>Comment le client paie : au comptoir (à la remise) ou en ligne (prestataire de paiement).</summary>
public enum PaymentMode
{
    Counter = 1,
    Online = 2,
}

/// <summary>Où en est la commande en ligne du point de vue de la vente en ligne (le cycle de la commande reste dans Orders).</summary>
public enum OnlineOrderState
{
    /// <summary>Validation manuelle : un membre du personnel doit l'accepter avant l'échéance.</summary>
    AwaitingAcceptance = 1,

    /// <summary>Prise en charge : acceptée automatiquement ou par le personnel, ou close par lui.</summary>
    Handled = 2,

    /// <summary>Non acceptée avant l'échéance : la commande a été refusée automatiquement.</summary>
    Expired = 3,

    /// <summary>Annulée par le client avant d'être acceptée.</summary>
    CancelledByCustomer = 4,

    /// <summary>Payée en ligne : en attente du paiement, refusée s'il n'est pas capturé avant l'échéance.</summary>
    AwaitingPayment = 5,
}

/// <summary>
/// Une commande passée par une application de vente : de quelle application et de quel canal elle vient, comment elle
/// est payée, et, en validation manuelle, l'échéance au-delà de laquelle elle est refusée. Aucune donnée personnelle :
/// le client est dans Customers, la commande dans Orders.
/// </summary>
public sealed class OnlineOrder : AggregateRoot<OnlineOrderId>, ITenantOwned
{
    public const int MaxClientKeyLength = 64;

    private OnlineOrder(
        OnlineOrderId id,
        TenantId tenantId,
        OrderId orderId,
        SalesAppId appId,
        SalesAppKind channel,
        LocationId locationId,
        PaymentMode paymentMode,
        OrderAcceptance acceptance,
        int acceptanceMinutes,
        DateTimeOffset placedAtUtc,
        DateTimeOffset? payBeforeUtc,
        DateTimeOffset? slotStartUtc,
        CustomerId? customerId,
        string? clientKey) : base(id)
    {
        SlotStartUtc = slotStartUtc;
        CustomerId = customerId;
        TenantId = tenantId;
        OrderId = orderId;
        AppId = appId;
        Channel = channel;
        LocationId = locationId;
        PaymentMode = paymentMode;
        Acceptance = acceptance;
        AcceptanceMinutes = acceptanceMinutes;
        PlacedAtUtc = placedAtUtc;
        if (payBeforeUtc is not null)
        {
            State = OnlineOrderState.AwaitingPayment;
            PayBeforeUtc = payBeforeUtc;
            NextDeadlineUtc = payBeforeUtc;
            ClientKey = clientKey;
        }
        else
        {
            StartAcceptance(placedAtUtc);
        }
    }

    // Réservé à l'ORM.
    private OnlineOrder()
    {
    }

    public TenantId TenantId { get; private set; }

    public OrderId OrderId { get; private set; }

    public SalesAppId AppId { get; private set; }

    public SalesAppKind Channel { get; private set; }

    public LocationId LocationId { get; private set; }

    public PaymentMode PaymentMode { get; private set; }

    public OrderAcceptance Acceptance { get; private set; }

    public OnlineOrderState State { get; private set; }

    public DateTimeOffset PlacedAtUtc { get; private set; }

    /// <summary>Délai de validation manuelle, figé à la commande ; il court à partir du paiement pour une commande payée en ligne.</summary>
    public int AcceptanceMinutes { get; private set; }

    /// <summary>Validation manuelle seulement : au-delà, la commande non acceptée est refusée.</summary>
    public DateTimeOffset? AcceptBeforeUtc { get; private set; }

    /// <summary>Paiement en ligne seulement : au-delà, la commande impayée est annulée.</summary>
    public DateTimeOffset? PayBeforeUtc { get; private set; }

    public DateTimeOffset? PaidAtUtc { get; private set; }

    /// <summary>L'échéance surveillée par le job (paiement ou acceptation) ; null quand il n'y a plus rien à attendre.</summary>
    public DateTimeOffset? NextDeadlineUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>Le créneau occupé par la commande (capacité par créneau) ; null pour une commande antérieure aux créneaux.</summary>
    public DateTimeOffset? SlotStartUtc { get; private set; }

    /// <summary>La commande occupe-t-elle encore son créneau ? Non si elle a été refusée faute d'acceptation ou annulée par le client.</summary>
    /// <summary>Le client de la commande (fiche Customers) ; null pour une borne ou une table sans coordonnées.</summary>
    public CustomerId? CustomerId { get; private set; }

    /// <summary>Empreinte de l'adresse IP du client, gardée seulement tant que le paiement en ligne est attendu.</summary>
    public string? ClientKey { get; private set; }

    public bool HoldsSlot => State is not (OnlineOrderState.Expired or OnlineOrderState.CancelledByCustomer);

    public bool IsAwaitingAcceptance => State == OnlineOrderState.AwaitingAcceptance;

    public bool IsAwaitingPayment => State == OnlineOrderState.AwaitingPayment;

    /// <summary>Payée en ligne et encaissée : une annulation ou un refus doit être remboursé.</summary>
    public bool IsPaidOnline => PaymentMode == PaymentMode.Online && PaidAtUtc is not null;

    /// <param name="acceptanceDelay">Délai de validation manuelle ; ignoré en acceptation automatique.</param>
    /// <param name="unpaidDelay">Paiement en ligne : délai pour payer ; ignoré au comptoir.</param>
    /// <param name="readyAtUtc">L'heure où la commande doit être prête : elle donne son créneau.</param>
    /// <param name="customerId">Le client, pour limiter ses commandes en attente de paiement.</param>
    /// <param name="clientKey">Empreinte de l'adresse IP, même usage ; gardée seulement pour un paiement en ligne.</param>
    public static OnlineOrder Record(
        TenantId tenantId,
        OrderId orderId,
        SalesApp app,
        LocationId locationId,
        PaymentMode paymentMode,
        OrderAcceptance acceptance,
        TimeSpan acceptanceDelay,
        TimeSpan unpaidDelay,
        IClock clock,
        DateTimeOffset? readyAtUtc = null,
        CustomerId? customerId = null,
        string? clientKey = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        app.EnsureSameTenant(tenantId);
        DomainException.ThrowIf(!Enum.IsDefined(paymentMode), OnlineOrderErrors.PaymentModeInvalid);
        DomainException.ThrowIf(!Enum.IsDefined(acceptance), OnlineOrderingErrors.AcceptanceInvalid);
        DomainException.ThrowIf(acceptance == OrderAcceptance.Manual && acceptanceDelay <= TimeSpan.Zero, OnlineOrderingErrors.AcceptanceDelayInvalid);
        DomainException.ThrowIf(paymentMode == PaymentMode.Online && unpaidDelay <= TimeSpan.Zero, OnlineOrderingErrors.ExpiryInvalid);
        DomainException.ThrowIf(clientKey is { Length: > MaxClientKeyLength }, OnlineOrderErrors.ClientKeyInvalid);

        var now = clock.UtcNow;
        return new OnlineOrder(
            OnlineOrderId.New(), tenantId, orderId, app.Id, app.Kind, locationId, paymentMode, acceptance,
            acceptance == OrderAcceptance.Manual ? (int)acceptanceDelay.TotalMinutes : 0, now,
            paymentMode == PaymentMode.Online ? now + unpaidDelay : null,
            readyAtUtc is { } ready ? OrderSlots.StartOf(ready) : null,
            customerId,
            string.IsNullOrEmpty(clientKey) ? null : clientKey);
    }

    /// <summary>L'échéance en cours (paiement ou acceptation) est-elle passée ?</summary>
    public bool IsOverdue(DateTimeOffset nowUtc) => (IsAwaitingPayment || IsAwaitingAcceptance) && NextDeadlineUtc <= nowUtc;

    /// <summary>
    /// Le paiement en ligne est encaissé. Acceptation automatique : prise en charge ; manuelle : le délai d'acceptation
    /// court à partir de maintenant. Renvoie vrai quand la commande doit être acceptée aussitôt. Sans effet si elle
    /// n'attendait pas son paiement.
    /// </summary>
    public bool MarkPaid(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!IsAwaitingPayment)
        {
            return false;
        }

        PaidAtUtc = clock.UtcNow;
        PayBeforeUtc = null;
        ClientKey = null;
        StartAcceptance(clock.UtcNow);
        return State == OnlineOrderState.Handled;
    }

    /// <summary>Le personnel a accepté (ou clos) la commande : plus rien à surveiller. Sans effet si déjà résolue.</summary>
    public void MarkHandled(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsAwaitingAcceptance || IsAwaitingPayment)
        {
            Resolve(OnlineOrderState.Handled, clock);
        }
    }

    /// <summary>Refusée automatiquement : seulement une commande encore en attente (paiement ou acceptation) dont l'échéance est passée.</summary>
    public void Expire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsAwaitingAcceptance && !IsAwaitingPayment, OnlineOrderErrors.NotAwaitingAcceptance);
        DomainException.ThrowIf(!IsOverdue(clock.UtcNow), OnlineOrderErrors.NotOverdue);
        Resolve(OnlineOrderState.Expired, clock);
    }

    /// <summary>Le client annule : seulement tant que la commande attend son paiement ou d'être acceptée.</summary>
    public void CancelByCustomer(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsAwaitingAcceptance && !IsAwaitingPayment, OnlineOrderErrors.NotCancellable);
        Resolve(OnlineOrderState.CancelledByCustomer, clock);
    }

    private void StartAcceptance(DateTimeOffset now)
    {
        if (Acceptance == OrderAcceptance.Manual)
        {
            State = OnlineOrderState.AwaitingAcceptance;
            AcceptBeforeUtc = now.AddMinutes(AcceptanceMinutes);
            NextDeadlineUtc = AcceptBeforeUtc;
        }
        else
        {
            State = OnlineOrderState.Handled;
            ResolvedAtUtc = now;
            NextDeadlineUtc = null;
        }
    }

    private void Resolve(OnlineOrderState state, IClock clock)
    {
        State = state;
        ResolvedAtUtc = clock.UtcNow;
        NextDeadlineUtc = null;
        ClientKey = null;
    }
}

public static class OnlineOrderErrors
{
    public static readonly DomainError PaymentModeInvalid = new("storefront.order.payment_mode_invalid", "The payment mode is not recognized.");
    public static readonly DomainError NotAwaitingAcceptance =
        new("storefront.order.not_awaiting_acceptance", "The order is no longer waiting to be accepted.", DomainErrorKind.Conflict);
    public static readonly DomainError ClientKeyInvalid = new("storefront.order.client_key_invalid", "The client fingerprint is too long.");
    public static readonly DomainError NotOverdue = new("storefront.order.not_overdue", "The acceptance deadline has not passed yet.");
    public static readonly DomainError NotCancellable =
        new("storefront.order.not_cancellable", "The order has already been accepted by the restaurant: contact it to cancel.", DomainErrorKind.Conflict);
}

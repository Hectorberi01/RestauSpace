namespace Reservations.Domain;

/// <summary>
/// Engagement de places pour un client sur un créneau. <see cref="StartAtUtc"/> est un instant (UTC) et ce module ne
/// connaît aucun fuseau horaire : l'Application convertit les heures locales aux frontières et vérifie les horaires
/// d'ouverture auprès du module Locations avant de demander une décision de disponibilité.
/// </summary>
public sealed class Reservation : AggregateRoot<ReservationId>, ITenantOwned
{
    public const int NotesMaxLength = 500;

    private Reservation(
        ReservationId id,
        TenantId tenantId,
        LocationId locationId,
        CustomerId? customerId,
        GuestContact guestContact,
        int partySize,
        Period slot,
        string? notes,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        CustomerId = customerId;
        GuestContact = guestContact;
        PartySize = partySize;
        StartAtUtc = slot.StartUtc;
        Duration = slot.Duration;
        Notes = notes;
        Status = ReservationStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Reservation()
    {
        GuestContact = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public GuestContact GuestContact { get; private set; }

    public int PartySize { get; private set; }

    public DateTimeOffset StartAtUtc { get; private set; }

    public TimeSpan Duration { get; private set; }

    public ReservationStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ReservationActor? CancelledBy { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Session de table (module Floor) ouverte pour le groupe à son installation, si elle a été fournie.</summary>
    public TableSessionId? TableSessionId { get; private set; }

    /// <summary>Vrai dès que les données personnelles du client ont été effacées de cette réservation.</summary>
    public bool IsGuestAnonymized => GuestContact.IsAnonymized;

    // Montants de l'acompte stockés à plat (montant + devise), comme les frais de livraison d'une commande.
    private decimal? _depositAmount;
    private string? _depositCurrency;
    private decimal? _depositSettled;

    /// <summary>Acompte demandé à la création ; null si la réservation n'en a pas.</summary>
    public Money? DepositAmount
    {
        get => _depositAmount is { } amount ? Money.Of(amount, Currency.FromCode(_depositCurrency!)) : null;
        private set
        {
            _depositAmount = value?.Amount;
            _depositCurrency = value?.Currency.Code;
        }
    }

    public DepositStatus? DepositStatus { get; private set; }

    /// <summary>Au-delà, une réservation en attente de son acompte est annulée et ses places rendues.</summary>
    public DateTimeOffset? DepositDueBeforeUtc { get; private set; }

    /// <summary>Ce que les paiements de l'acompte détiennent encore (encaissé moins remboursé), d'après Payments.</summary>
    public Money? DepositSettled
    {
        get => _depositSettled is { } settled && _depositCurrency is not null ? Money.Of(settled, Currency.FromCode(_depositCurrency)) : null;
        private set => _depositSettled = value?.Amount;
    }

    public bool HasDeposit => _depositAmount is not null;

    public Period Slot => Period.Create(StartAtUtc, StartAtUtc + Duration);

    public bool HoldsCapacity => Status.HoldsCapacity();

    /// <param name="tenantId">Tenant pour lequel la réservation est créée ; doit posséder <paramref name="location"/> et <paramref name="customer"/>.</param>
    /// <param name="location">Établissement réservé, chargé par l'Application ; doit être opérationnel (Active).</param>
    /// <param name="customer">Client CRM rattaché à la réservation, ou null pour un client sans fiche.</param>
    /// <param name="slot">Instant de début et durée ; utiliser <see cref="ReservationPolicy.SlotFor"/> pour la durée par défaut.</param>
    /// <param name="availability">Décision fraîche de <see cref="ReservationAvailabilityPolicy"/> pour cet établissement, ce créneau et cette taille de groupe ; consommée par une création réussie.</param>
    public static Reservation Create(
        TenantId tenantId,
        LocationReference location,
        CustomerReference? customer,
        GuestContact guestContact,
        int partySize,
        Period slot,
        string? notes,
        ReservationPolicy policy,
        AvailabilityDecision availability,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(guestContact);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var now = clock.UtcNow;
        EnsureValidPartySize(partySize, policy);
        var normalizedNotes = NormalizeNotes(notes);
        EnsureBookable(policy, slot, now);

        var reservation = new Reservation(
            ReservationId.New(), tenantId, location.LocationId, customer?.CustomerId, guestContact, partySize, slot, normalizedNotes, now);
        reservation.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!location.IsOperational, ReservationErrors.LocationNotOperational);
        if (customer is not null)
        {
            reservation.EnsureSameTenant(customer.TenantId);
            DomainException.ThrowIf(customer.IsAnonymized, ReservationErrors.CustomerAnonymized);
        }

        // Vérifié en dernier exprès : la décision n'est consommée que si rien d'autre ne peut refuser la réservation.
        availability.Consume(location.LocationId, slot, partySize, reservationId: null);

        reservation.Raise(new ReservationCreated(
            tenantId, reservation.Id, reservation.LocationId, reservation.CustomerId, partySize, reservation.StartAtUtc, reservation.Duration, now));
        return reservation;
    }

    /// <summary>
    /// Rattache la réservation à une fiche client du même tenant, ou l'en détache (null), tant qu'elle n'est pas
    /// terminée. L'instantané de contact de la réservation ne change pas. Idempotent.
    /// </summary>
    public void ChangeCustomer(CustomerReference? customer, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        if (customer is not null)
        {
            this.EnsureSameTenant(customer.TenantId);
            DomainException.ThrowIf(customer.IsAnonymized, ReservationErrors.CustomerAnonymized);
        }

        var next = customer?.CustomerId;
        if (next == CustomerId)
        {
            return;
        }

        var previous = CustomerId;
        CustomerId = next;
        Raise(new ReservationCustomerChanged(TenantId, Id, LocationId, previous, next, clock.UtcNow));
    }

    /// <summary>
    /// Demande un acompte (règle de l'établissement), juste après la création et avant toute confirmation : la
    /// réservation reste en attente jusqu'au paiement, qui la confirme. Non payé à <paramref name="dueBeforeUtc"/>, elle
    /// est annulée (<see cref="ExpireUnpaidDeposit"/>).
    /// </summary>
    public void RequireDeposit(Money amount, DateTimeOffset dueBeforeUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != ReservationStatus.Pending, ReservationErrors.NotPending);
        DomainException.ThrowIf(HasDeposit, DepositErrors.AlreadyRequired);
        DomainException.ThrowIf(!amount.IsPositive || amount.Amount > DepositRule.MaxAmount, DepositErrors.AmountInvalid);
        var now = clock.UtcNow;
        DomainException.ThrowIf(dueBeforeUtc <= now, DepositErrors.DueBeforeInvalid);

        DepositAmount = amount;
        DepositStatus = Domain.DepositStatus.AwaitingPayment;
        DepositDueBeforeUtc = dueBeforeUtc;
        DepositSettled = Money.Zero(amount.Currency);
        Raise(new ReservationDepositRequested(TenantId, Id, LocationId, amount, dueBeforeUtc, now));
    }

    /// <summary>
    /// Ce que les paiements de l'acompte détiennent maintenant (Payments, à chaque capture ou remboursement). Réglé en
    /// entier : l'acompte est payé et une réservation en attente est confirmée ; arrivé après l'annulation de la
    /// réservation, il est aussitôt à rembourser. Revenu à zéro après un remboursement : remboursé. Idempotent.
    /// </summary>
    public void RecordDepositSettlement(Money settled, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settled);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!HasDeposit, DepositErrors.NotRequired);
        DomainException.ThrowIf(settled.Currency != DepositAmount!.Currency, DepositErrors.CurrencyMismatch);
        if (DepositStatus == Domain.DepositStatus.Applied)
        {
            return;
        }

        var now = clock.UtcNow;
        DepositSettled = settled.IsNegative ? Money.Zero(settled.Currency) : settled;
        var fullyPaid = DepositSettled! >= DepositAmount;
        switch (DepositStatus)
        {
            case Domain.DepositStatus.AwaitingPayment or Domain.DepositStatus.Waived when fullyPaid:
                DepositStatus = Domain.DepositStatus.Paid;
                Raise(new ReservationDepositPaid(TenantId, Id, LocationId, DepositSettled, now));
                if (Status == ReservationStatus.Pending)
                {
                    Status = ReservationStatus.Confirmed;
                    Raise(new ReservationConfirmed(TenantId, Id, LocationId, now));
                }
                else if (Status is ReservationStatus.Cancelled)
                {
                    // Payé trop tard (la réservation a expiré entre-temps) : l'argent repart.
                    DepositStatus = Domain.DepositStatus.RefundRequested;
                    Raise(new ReservationDepositRefundRequested(TenantId, Id, LocationId, now));
                }

                break;
            case Domain.DepositStatus.Paid or Domain.DepositStatus.Forfeited or Domain.DepositStatus.RefundRequested when !DepositSettled.IsPositive:
                DepositStatus = Domain.DepositStatus.Refunded;
                Raise(new ReservationDepositRefunded(TenantId, Id, LocationId, now));
                break;
        }
    }

    /// <summary>L'équipe renonce à l'acompte attendu : la réservation ne sera plus annulée faute de paiement.</summary>
    public void WaiveDeposit(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(DepositStatus != Domain.DepositStatus.AwaitingPayment, DepositErrors.NotAwaitingPayment);
        DepositStatus = Domain.DepositStatus.Waived;
        Raise(new ReservationDepositWaived(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>L'acompte payé a été déduit de l'addition du repas (Payments l'a rattaché à la commande).</summary>
    public void MarkDepositApplied(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (DepositStatus == Domain.DepositStatus.Applied)
        {
            return;
        }

        DomainException.ThrowIf(DepositStatus is not (Domain.DepositStatus.Paid or Domain.DepositStatus.Forfeited), DepositErrors.NotPaid);
        DepositStatus = Domain.DepositStatus.Applied;
        Raise(new ReservationDepositApplied(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>Rembourse un acompte payé ou gardé (geste de l'équipe) : Payments le rend au client.</summary>
    public void RequestDepositRefund(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(DepositStatus is not (Domain.DepositStatus.Paid or Domain.DepositStatus.Forfeited), DepositErrors.NotRefundable);
        DomainException.ThrowIf(Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated, DepositErrors.NotRefundable);
        DepositStatus = Domain.DepositStatus.RefundRequested;
        Raise(new ReservationDepositRefundRequested(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>
    /// Annule une réservation encore en attente de son acompte une fois le délai de paiement passé (le système, pas de
    /// délai d'annulation). Les places sont rendues.
    /// </summary>
    public void ExpireUnpaidDeposit(ReservationPolicy policy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != ReservationStatus.Pending || DepositStatus != Domain.DepositStatus.AwaitingPayment, DepositErrors.NotAwaitingPayment);
        DomainException.ThrowIf(clock.UtcNow < DepositDueBeforeUtc, DepositErrors.NotDue);
        Cancel(ReservationActor.System, UnpaidDepositReason, policy, clock);
    }

    public const string UnpaidDepositReason = "Acompte non payé dans le délai";

    public void Confirm(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ReservationStatus.Pending, ReservationErrors.NotPending);

        Status = ReservationStatus.Confirmed;
        Raise(new ReservationConfirmed(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <param name="availability">Décision fraîche pour le nouveau créneau, évaluée en excluant cette réservation des réservations existantes ; consommée par un déplacement réussi.</param>
    public void Reschedule(
        Period newSlot,
        ReservationActor requestedBy,
        ReservationPolicy policy,
        AvailabilityDecision availability,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newSlot);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        EnsureModifiable(requestedBy, policy, now);
        if (newSlot == Slot)
        {
            // Rien ne bouge : la fenêtre de réservation et la capacité ont été vérifiées quand ce créneau a été pris.
            return;
        }

        EnsureBookable(policy, newSlot, now);
        availability.Consume(LocationId, newSlot, PartySize, Id);

        var previousStart = StartAtUtc;
        var previousDuration = Duration;
        StartAtUtc = newSlot.StartUtc;
        Duration = newSlot.Duration;
        Raise(new ReservationRescheduled(TenantId, Id, LocationId, previousStart, previousDuration, StartAtUtc, Duration, now));
    }

    /// <param name="availability">Décision fraîche pour le créneau actuel et la nouvelle taille, évaluée en excluant cette réservation ; consommée par un changement réussi.</param>
    public void ChangePartySize(
        int partySize,
        ReservationActor requestedBy,
        ReservationPolicy policy,
        AvailabilityDecision availability,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        EnsureModifiable(requestedBy, policy, now);
        if (partySize == PartySize)
        {
            // Rien ne change : la décision n'est ni examinée ni consommée.
            return;
        }

        EnsureValidPartySize(partySize, policy);
        availability.Consume(LocationId, Slot, partySize, Id);

        var previous = PartySize;
        PartySize = partySize;
        Raise(new ReservationPartySizeChanged(TenantId, Id, LocationId, previous, partySize, now));
    }

    /// <param name="tableSessionId">Session ouverte sur la table pour ce groupe, ou null si aucune n'est suivie.</param>
    public void Seat(TableSessionId? tableSessionId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ReservationStatus.Confirmed, ReservationErrors.NotConfirmed);
        if (tableSessionId is { } sessionId)
        {
            Guard.NotEmpty(sessionId);
        }

        Status = ReservationStatus.Seated;
        TableSessionId = tableSessionId;
        Raise(new GuestSeated(TenantId, Id, LocationId, PartySize, tableSessionId, clock.UtcNow));
    }

    public void Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ReservationStatus.Seated, ReservationErrors.NotSeated);

        Status = ReservationStatus.Completed;
        Raise(new ReservationCompleted(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>
    /// Un client peut annuler jusqu'à la limite d'annulation de la policy ; le personnel et le système peuvent annuler
    /// à tout moment avant l'installation.
    /// </summary>
    /// <param name="keepDeposit">
    /// Un acompte payé est remboursé, sauf si l'équipe décide de le garder (annulation tardive) ; le client et le système
    /// ne peuvent pas le garder.
    /// </param>
    public void Cancel(ReservationActor cancelledBy, string? reason, ReservationPolicy policy, IClock clock, bool keepDeposit = false)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureKnownActor(cancelledBy);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status == ReservationStatus.Seated, ReservationErrors.NotCancellableAfterSeating);

        var now = clock.UtcNow;
        DomainException.ThrowIf(
            cancelledBy.IsBoundByCutoff() && !policy.CanCancelAt(StartAtUtc, now),
            ReservationErrors.CancellationCutoffPassed);

        Status = ReservationStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Raise(new ReservationCancelled(TenantId, Id, LocationId, cancelledBy, CancellationReason, now));
        if (DepositStatus == Domain.DepositStatus.Paid)
        {
            if (keepDeposit && cancelledBy == ReservationActor.Staff)
            {
                DepositStatus = Domain.DepositStatus.Forfeited;
                Raise(new ReservationDepositForfeited(TenantId, Id, LocationId, now));
            }
            else
            {
                DepositStatus = Domain.DepositStatus.RefundRequested;
                Raise(new ReservationDepositRefundRequested(TenantId, Id, LocationId, now));
            }
        }
    }

    public void MarkNoShow(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status != ReservationStatus.Confirmed, ReservationErrors.NotConfirmed);

        var now = clock.UtcNow;
        DomainException.ThrowIf(now < StartAtUtc, ReservationErrors.NoShowBeforeStart);

        Status = ReservationStatus.NoShow;
        Raise(new ReservationNoShow(TenantId, Id, LocationId, CustomerId, now));
        if (DepositStatus == Domain.DepositStatus.Paid)
        {
            DepositStatus = Domain.DepositStatus.Forfeited;
            Raise(new ReservationDepositForfeited(TenantId, Id, LocationId, now));
        }
    }

    /// <summary>
    /// Effacement des données personnelles du client (le client a été anonymisé) : l'instantané de contact devient
    /// <see cref="GuestContact.Anonymized"/> et les notes sont vidées. Pas un changement métier, donc permis dans
    /// tous les statuts, terminaux compris ; <see cref="CustomerId"/> est conservé. Idempotent.
    /// </summary>
    public void AnonymizeGuest(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsGuestAnonymized)
        {
            return;
        }

        GuestContact = GuestContact.Anonymized;
        Notes = null;
        Raise(new ReservationGuestAnonymized(TenantId, Id, LocationId, CustomerId, clock.UtcNow));
    }

    /// <summary>Cette réservation comme entrée de <see cref="ReservationAvailabilityPolicy"/> ; seulement tant qu'elle occupe de la capacité.</summary>
    public ExistingBooking ToExistingBooking()
    {
        DomainException.ThrowIf(!HoldsCapacity, ReservationErrors.DoesNotHoldCapacity);
        return ExistingBooking.Create(Id, Slot, PartySize);
    }

    private void EnsureNotTerminal() =>
        DomainException.ThrowIf(Status.IsTerminal(), ReservationErrors.AlreadyTerminal);

    private static void EnsureKnownActor(ReservationActor actor) =>
        DomainException.ThrowIf(!Enum.IsDefined(actor), ReservationErrors.ActorInvalid);

    private void EnsureModifiable(ReservationActor requestedBy, ReservationPolicy policy, DateTimeOffset now)
    {
        EnsureKnownActor(requestedBy);
        EnsureNotTerminal();
        DomainException.ThrowIf(Status == ReservationStatus.Seated, ReservationErrors.NotModifiableAfterSeating);
        DomainException.ThrowIf(
            requestedBy.IsBoundByCutoff() && !policy.CanCancelAt(StartAtUtc, now),
            ReservationErrors.ModificationCutoffPassed);
    }

    private static void EnsureBookable(ReservationPolicy policy, Period slot, DateTimeOffset now)
    {
        DomainException.ThrowIf(policy.IsTooShort(slot.Duration), ReservationErrors.DurationTooShort);
        DomainException.ThrowIf(policy.IsTooLong(slot.Duration), ReservationErrors.DurationTooLong);
        DomainException.ThrowIf(policy.IsTooSoon(slot.StartUtc, now), ReservationErrors.LeadTimeNotRespected);
        DomainException.ThrowIf(policy.IsBeyondHorizon(slot.StartUtc, now), ReservationErrors.BeyondBookingHorizon);
    }

    private static void EnsureValidPartySize(int partySize, ReservationPolicy policy)
    {
        DomainException.ThrowIf(partySize <= 0, ReservationErrors.PartySizeNotPositive);
        DomainException.ThrowIf(partySize > policy.MaxPartySize, ReservationErrors.PartySizeTooLarge);
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var trimmed = notes.Trim();
        DomainException.ThrowIf(trimmed.Length > NotesMaxLength, ReservationErrors.NotesTooLong);
        return trimmed;
    }
}

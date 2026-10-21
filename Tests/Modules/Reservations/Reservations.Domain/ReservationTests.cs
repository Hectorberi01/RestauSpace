namespace Reservations.Domain;

public sealed class ReservationTests
{
    private const int Capacity = 20;

    // Délai minimal 1 h, horizon 30 jours, tables de 2 h, limite client 2 h avant le début.
    private static readonly ReservationPolicy Policy = ReservationPolicy.Create(
        TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromHours(2), TimeSpan.FromHours(2));

    private static readonly DateTimeOffset Tomorrow8Pm = new(2026, 1, 16, 20, 0, 0, TimeSpan.Zero);

    private static readonly GuestContact Contact =
        GuestContact.Create("Awa Diallo", PhoneNumber.Create("+22997000000"), email: null);

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();

    private static Period SlotAt(DateTimeOffset start) => Policy.SlotFor(start);

    private AvailabilityDecision Decide(
        Period slot,
        int partySize,
        int capacity = Capacity,
        IEnumerable<ExistingBooking>? existing = null,
        ReservationId? excluded = null) =>
        ReservationAvailabilityPolicy.Evaluate(_locationId, slot, partySize, capacity, existing ?? [], excluded);

    private LocationReference Location(bool isOperational = true) => new(_tenantId, _locationId, isOperational);

    private CustomerReference Customer(CustomerId customerId, bool isAnonymized = false) => new(_tenantId, customerId, isAnonymized);

    private Reservation Create(int partySize = 4, Period? slot = null, string? notes = null, CustomerId? customerId = null)
    {
        var theSlot = slot ?? SlotAt(Tomorrow8Pm);
        var customer = customerId is { } id ? Customer(id) : null;
        return Reservation.Create(
            _tenantId, Location(), customer, Contact, partySize, theSlot, notes, Policy, Decide(theSlot, partySize), _clock);
    }

    private Reservation CreateWith(
        AvailabilityDecision decision,
        LocationReference? location = null,
        CustomerReference? customer = null,
        ReservationPolicy? policy = null,
        TenantId? tenantId = null) =>
        Reservation.Create(
            tenantId ?? _tenantId, location ?? Location(), customer, Contact, decision.PartySize, decision.Slot, null, policy ?? Policy, decision, _clock);

    private Reservation Pending(int partySize = 4)
    {
        var reservation = Create(partySize);
        reservation.ClearDomainEvents();
        return reservation;
    }

    private Reservation Confirmed(int partySize = 4)
    {
        var reservation = Pending(partySize);
        reservation.Confirm(_clock);
        reservation.ClearDomainEvents();
        return reservation;
    }

    private Reservation Seated()
    {
        var reservation = Confirmed();
        reservation.Seat(null, _clock);
        reservation.ClearDomainEvents();
        return reservation;
    }

    private Reservation InStatus(ReservationStatus status)
    {
        Reservation reservation;
        switch (status)
        {
            case ReservationStatus.Pending:
                return Pending();
            case ReservationStatus.Confirmed:
                return Confirmed();
            case ReservationStatus.Seated:
                return Seated();
            case ReservationStatus.Completed:
                reservation = Seated();
                reservation.Complete(_clock);
                break;
            case ReservationStatus.Cancelled:
                reservation = Confirmed();
                reservation.Cancel(ReservationActor.Customer, null, Policy, _clock);
                break;
            default:
                reservation = Confirmed();
                _clock.Set(reservation.StartAtUtc.AddMinutes(20));
                reservation.MarkNoShow(_clock);
                break;
        }

        reservation.ClearDomainEvents();
        Assert.Equal(status, reservation.Status);
        return reservation;
    }

    // ---- Create -------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_builds_a_pending_reservation_and_raises_ReservationCreated()
    {
        var customerId = CustomerId.New();

        var reservation = Create(partySize: 4, notes: "  Anniversaire  ", customerId: customerId);

        Assert.Equal(_tenantId, reservation.TenantId);
        Assert.Equal(_locationId, reservation.LocationId);
        Assert.Equal(customerId, reservation.CustomerId);
        Assert.Equal(Contact, reservation.GuestContact);
        Assert.Equal(4, reservation.PartySize);
        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.FromHours(2), reservation.Duration);
        Assert.Equal(SlotAt(Tomorrow8Pm), reservation.Slot);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal("Anniversaire", reservation.Notes);
        Assert.Equal(_clock.UtcNow, reservation.CreatedAtUtc);
        Assert.Null(reservation.CancelledBy);
        var raised = DomainAssert.Raised<ReservationCreated>(reservation);
        Assert.Equal(
            new ReservationCreated(
                _tenantId, reservation.Id, _locationId, customerId, 4, Tomorrow8Pm, TimeSpan.FromHours(2), _clock.UtcNow),
            raised);
    }

    [Fact]
    public void Create_accepts_a_walk_in_guest_without_customer_id_and_without_notes()
    {
        var reservation = Create(notes: "   ");

        Assert.Null(reservation.CustomerId);
        Assert.Null(reservation.Notes);
    }

    [Fact]
    public void Create_stores_the_start_as_a_utc_instant_whatever_the_offset_given()
    {
        var localStart = new DateTimeOffset(2026, 1, 16, 21, 0, 0, TimeSpan.FromHours(1));

        var reservation = Create(slot: Period.Create(localStart, localStart.AddHours(2)));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.Zero, reservation.StartAtUtc.Offset);
    }

    [Fact]
    public void Create_accepts_an_explicit_duration_different_from_the_policy_default()
    {
        var reservation = Create(slot: Period.Create(Tomorrow8Pm, Tomorrow8Pm.AddMinutes(90)));

        Assert.Equal(TimeSpan.FromMinutes(90), reservation.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Create_rejects_a_party_size_that_is_not_positive(int partySize)
    {
        var slot = SlotAt(Tomorrow8Pm);
        var decisionForOne = Decide(slot, 1);

        DomainAssert.Throws(
            ReservationErrors.PartySizeNotPositive,
            () => Reservation.Create(_tenantId, Location(), null, Contact, partySize, slot, null, Policy, decisionForOne, _clock));
    }

    [Fact]
    public void Create_enforces_the_notes_length_limit()
    {
        Assert.Equal(Reservation.NotesMaxLength, Create(notes: new string('n', Reservation.NotesMaxLength)).Notes!.Length);
        DomainAssert.Throws(
            ReservationErrors.NotesTooLong,
            () => Create(notes: new string('n', Reservation.NotesMaxLength + 1)));
    }

    [Fact]
    public void Create_rejects_a_slot_starting_before_the_minimum_lead_time()
    {
        var tooSoon = _clock.UtcNow.AddHours(1).AddSeconds(-1);

        DomainAssert.Throws(ReservationErrors.LeadTimeNotRespected, () => Create(slot: SlotAt(tooSoon)));
    }

    [Fact]
    public void Create_rejects_a_slot_in_the_past()
    {
        DomainAssert.Throws(ReservationErrors.LeadTimeNotRespected, () => Create(slot: SlotAt(_clock.UtcNow.AddHours(-3))));
    }

    [Fact]
    public void Create_accepts_a_slot_starting_exactly_at_the_minimum_lead_time()
    {
        var earliest = _clock.UtcNow.AddHours(1);

        Assert.Equal(earliest, Create(slot: SlotAt(earliest)).StartAtUtc);
    }

    [Fact]
    public void Create_accepts_a_slot_exactly_at_the_booking_horizon_and_rejects_one_second_later()
    {
        var latest = _clock.UtcNow.AddDays(30);

        Assert.Equal(latest, Create(slot: SlotAt(latest)).StartAtUtc);
        DomainAssert.Throws(ReservationErrors.BeyondBookingHorizon, () => Create(slot: SlotAt(latest.AddSeconds(1))));
    }

    [Fact]
    public void Create_rejects_a_slot_without_enough_remaining_capacity()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var refused = Decide(slot, partySize: 4, capacity: 3);

        Assert.False(refused.IsAvailable);
        DomainAssert.Throws(
            ReservationErrors.CapacityInsufficient,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, refused, _clock));
    }

    [Fact]
    public void Create_rejects_a_decision_made_for_another_location()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var elsewhere = ReservationAvailabilityPolicy.Evaluate(LocationId.New(), slot, 4, Capacity, []);

        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, elsewhere, _clock));
    }

    [Fact]
    public void Create_rejects_a_decision_made_for_another_slot()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var otherSlot = Decide(SlotAt(Tomorrow8Pm.AddHours(1)), 4);
        var shorter = Decide(Period.Create(Tomorrow8Pm, Tomorrow8Pm.AddHours(1)), 4);

        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, otherSlot, _clock));
        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, shorter, _clock));
    }

    [Fact]
    public void Create_rejects_a_decision_made_for_another_party_size()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var forTwo = Decide(slot, 2);

        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, forTwo, _clock));
    }

    [Fact]
    public void Create_rejects_a_decision_that_ignored_some_existing_reservation()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var ignoringSomeone = Decide(slot, 4, excluded: ReservationId.New());

        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, ignoringSomeone, _clock));
    }

    // ---- Durcissement : ids, références, décision à usage unique, bornes -----------------------------------------

    [Fact]
    public void Create_rejects_an_empty_tenant_and_empty_foreign_ids()
    {
        var decision = Decide(SlotAt(Tomorrow8Pm), 4);

        DomainAssert.Throws(CommonErrors.EmptyId, () => CreateWith(decision, tenantId: default(TenantId)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(_tenantId, default, IsOperational: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, _locationId, IsOperational: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(_tenantId, default, IsAnonymized: false));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(default, CustomerId.New(), IsAnonymized: false));
        Assert.False(decision.IsConsumed);
    }

    [Fact]
    public void Create_rejects_a_location_or_a_customer_of_another_tenant()
    {
        var decision = Decide(SlotAt(Tomorrow8Pm), 4);
        var foreignLocation = new LocationReference(TenantId.New(), _locationId, IsOperational: true);
        var foreignCustomer = new CustomerReference(TenantId.New(), CustomerId.New(), IsAnonymized: false);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => CreateWith(decision, location: foreignLocation));
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => CreateWith(decision, customer: foreignCustomer));
        Assert.False(decision.IsConsumed);
    }

    [Fact]
    public void Create_rejects_a_location_that_is_not_operational()
    {
        var decision = Decide(SlotAt(Tomorrow8Pm), 4);

        DomainAssert.Throws(
            ReservationErrors.LocationNotOperational,
            () => CreateWith(decision, location: Location(isOperational: false)));
        Assert.False(decision.IsConsumed);
    }

    [Fact]
    public void Create_rejects_an_anonymized_customer_and_stores_the_id_of_a_live_one()
    {
        var customerId = CustomerId.New();
        var decision = Decide(SlotAt(Tomorrow8Pm), 4);

        DomainAssert.Throws(
            ReservationErrors.CustomerAnonymized,
            () => CreateWith(decision, customer: Customer(customerId, isAnonymized: true)));
        Assert.False(decision.IsConsumed);

        Assert.Equal(customerId, CreateWith(decision, customer: Customer(customerId)).CustomerId);
    }

    [Fact]
    public void Create_rejects_null_references()
    {
        var decision = Decide(SlotAt(Tomorrow8Pm), 4);

        Assert.Throws<ArgumentNullException>(
            () => Reservation.Create(_tenantId, null!, null, Contact, 4, decision.Slot, null, Policy, decision, _clock));
    }

    [Fact]
    public void Create_consumes_the_decision_which_cannot_create_a_second_reservation()
    {
        var decision = Decide(SlotAt(Tomorrow8Pm), 4, capacity: 4);
        Assert.False(decision.IsConsumed);

        CreateWith(decision);

        Assert.True(decision.IsConsumed);
        DomainAssert.Throws(ReservationErrors.AvailabilityDecisionAlreadyUsed, () => CreateWith(decision));
    }

    [Fact]
    public void A_decision_used_by_Create_cannot_be_replayed_by_Reschedule_or_ChangePartySize()
    {
        var reservation = Pending(partySize: 4);
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var forNewSlot = Decide(newSlot, 4);
        CreateWith(forNewSlot);

        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionAlreadyUsed,
            () => reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, forNewSlot, _clock));

        var forSix = Decide(reservation.Slot, 6);
        CreateWith(forSix);
        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionAlreadyUsed,
            () => reservation.ChangePartySize(6, ReservationActor.Staff, Policy, forSix, _clock));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_and_ChangePartySize_consume_their_decision()
    {
        var reservation = Confirmed(partySize: 4);
        var other = Confirmed(partySize: 4);
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var move = Decide(newSlot, 4);

        reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, move, _clock);

        Assert.True(move.IsConsumed);
        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionAlreadyUsed,
            () => other.Reschedule(newSlot, ReservationActor.Staff, Policy, move, _clock));

        var resize = Decide(other.Slot, 6);
        other.ChangePartySize(6, ReservationActor.Staff, Policy, resize, _clock);

        Assert.True(resize.IsConsumed);
        DomainAssert.Throws(ReservationErrors.AvailabilityDecisionAlreadyUsed, () => CreateWith(resize));
    }

    [Fact]
    public void A_rejected_use_does_not_consume_the_decision()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var decision = Decide(slot, 4);
        var reservation = Confirmed(partySize: 4);

        // Incohérence : la décision a été prise pour 4 convives, pas 5, et pas pour le créneau du lendemain.
        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => Reservation.Create(_tenantId, Location(), null, Contact, 5, slot, null, Policy, decision, _clock));
        DomainAssert.Throws(
            ReservationErrors.AvailabilityDecisionMismatch,
            () => reservation.Reschedule(SlotAt(Tomorrow8Pm.AddDays(1)), ReservationActor.Staff, Policy, decision, _clock));
        Assert.False(decision.IsConsumed);

        var refused = Decide(slot, 4, capacity: 3);
        DomainAssert.Throws(ReservationErrors.CapacityInsufficient, () => CreateWith(refused));
        Assert.False(refused.IsConsumed);

        Assert.Equal(ReservationStatus.Pending, CreateWith(decision).Status);
        Assert.True(decision.IsConsumed);
    }

    [Fact]
    public void Create_and_ChangePartySize_enforce_the_maximum_party_size_of_the_policy()
    {
        var slot = SlotAt(Tomorrow8Pm);
        var atLimit = Decide(slot, Policy.MaxPartySize, capacity: 100);
        var overLimit = Decide(slot, Policy.MaxPartySize + 1, capacity: 100);
        var huge = Decide(slot, int.MaxValue, capacity: 100);

        Assert.Equal(ReservationPolicy.DefaultMaxPartySize, CreateWith(atLimit).PartySize);
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => CreateWith(overLimit));
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => CreateWith(huge));
        Assert.False(overLimit.IsConsumed);

        var reservation = Confirmed(partySize: 4);
        var resize = Decide(reservation.Slot, int.MaxValue, capacity: 100, excluded: reservation.Id);
        DomainAssert.Throws(
            ReservationErrors.PartySizeTooLarge,
            () => reservation.ChangePartySize(int.MaxValue, ReservationActor.Staff, Policy, resize, _clock));
        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void A_party_of_int_MaxValue_is_never_booked_even_with_a_policy_at_its_ceiling()
    {
        var widest = ReservationPolicy.Create(
            TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromHours(2), TimeSpan.FromHours(2),
            maxPartySize: ReservationPolicy.MaxPartySizeCeiling);
        var huge = Decide(SlotAt(Tomorrow8Pm), int.MaxValue, existing: [ExistingBooking.Create(ReservationId.New(), SlotAt(Tomorrow8Pm), 1)]);

        Assert.False(huge.IsAvailable);
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => CreateWith(huge, policy: widest));
    }

    [Fact]
    public void Create_and_Reschedule_enforce_the_duration_bounds_of_the_policy()
    {
        var oneTick = Period.Create(Tomorrow8Pm, Tomorrow8Pm.AddTicks(1));
        var justUnder = Period.Create(Tomorrow8Pm, Tomorrow8Pm + Policy.MinDuration - TimeSpan.FromSeconds(1));
        var shortest = Period.Create(Tomorrow8Pm, Tomorrow8Pm + Policy.MinDuration);
        var longest = Period.Create(Tomorrow8Pm, Tomorrow8Pm + Policy.MaxDuration);
        var justOver = Period.Create(Tomorrow8Pm, Tomorrow8Pm + Policy.MaxDuration + TimeSpan.FromSeconds(1));
        var oneYear = Period.Create(Tomorrow8Pm, Tomorrow8Pm.AddDays(365));

        DomainAssert.Throws(ReservationErrors.DurationTooShort, () => Create(slot: oneTick));
        DomainAssert.Throws(ReservationErrors.DurationTooShort, () => Create(slot: justUnder));
        DomainAssert.Throws(ReservationErrors.DurationTooLong, () => Create(slot: justOver));
        DomainAssert.Throws(ReservationErrors.DurationTooLong, () => Create(slot: oneYear));
        Assert.Equal(Policy.MinDuration, Create(slot: shortest).Duration);
        Assert.Equal(Policy.MaxDuration, Create(slot: longest).Duration);

        var reservation = Confirmed();
        foreach (var (slot, error) in new[] { (oneTick, ReservationErrors.DurationTooShort), (oneYear, ReservationErrors.DurationTooLong) })
        {
            var decision = Decide(slot, 4, excluded: reservation.Id);
            DomainAssert.Throws(error, () => reservation.Reschedule(slot, ReservationActor.Staff, Policy, decision, _clock));
            Assert.False(decision.IsConsumed);
        }

        Assert.Equal(TimeSpan.FromHours(2), reservation.Duration);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_to_the_same_size_neither_examines_nor_consumes_the_decision()
    {
        var reservation = Confirmed(partySize: 4);
        var unrelated = ReservationAvailabilityPolicy.Evaluate(LocationId.New(), SlotAt(Tomorrow8Pm.AddDays(3)), 9, 0, []);
        Assert.False(unrelated.IsAvailable);

        reservation.ChangePartySize(4, ReservationActor.Staff, Policy, unrelated, _clock);

        Assert.False(unrelated.IsConsumed);
        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_to_the_same_slot_does_not_consume_the_decision()
    {
        var reservation = Confirmed();
        var decision = Decide(reservation.Slot, 4, excluded: reservation.Id);

        reservation.Reschedule(reservation.Slot, ReservationActor.Staff, Policy, decision, _clock);

        Assert.False(decision.IsConsumed);
    }

    [Fact]
    public void Seat_rejects_an_empty_table_session_id()
    {
        var reservation = Confirmed();

        DomainAssert.Throws(CommonErrors.EmptyId, () => reservation.Seat(default(TableSessionId), _clock));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Status_and_actor_helpers_reject_undefined_values()
    {
        DomainAssert.Throws(ReservationErrors.StatusUnknown, () => ((ReservationStatus)0).HoldsCapacity());
        DomainAssert.Throws(ReservationErrors.StatusUnknown, () => ((ReservationStatus)42).IsTerminal());
        DomainAssert.Throws(ReservationErrors.ActorInvalid, () => ((ReservationActor)0).IsBoundByCutoff());
    }

    [Fact]
    public void Capacity_holding_statuses_cannot_be_altered_through_a_downcast()
    {
        var statuses = Assert.IsAssignableFrom<IList<ReservationStatus>>(ReservationStatusExtensions.CapacityHoldingStatuses);

        Assert.False(statuses is ReservationStatus[]);
        Assert.Throws<NotSupportedException>(() => statuses.Add(ReservationStatus.Cancelled));
        Assert.Throws<NotSupportedException>(() => statuses[0] = ReservationStatus.Cancelled);
        Assert.Equal(3, ReservationStatusExtensions.CapacityHoldingStatuses.Count);
    }

    /// <summary>Compte les lectures : une opération doit lire l'horloge une seule fois pour que tous ses horodatages
    /// concordent.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                var now = _now;
                _now = _now.AddSeconds(1);
                return now;
            }
        }
    }

    [Fact]
    public void Every_operation_reads_the_clock_once()
    {
        var clock = new TickingClock(FakeClock.DefaultNow);
        var slot = SlotAt(Tomorrow8Pm);

        var reservation = Reservation.Create(_tenantId, Location(), null, Contact, 4, slot, null, Policy, Decide(slot, 4), clock);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(reservation.CreatedAtUtc, DomainAssert.Raised<ReservationCreated>(reservation).OccurredAtUtc);

        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        reservation.Reschedule(newSlot, ReservationActor.Customer, Policy, Decide(newSlot, 4), clock);
        Assert.Equal(2, clock.Reads);

        reservation.ChangePartySize(5, ReservationActor.Customer, Policy, Decide(newSlot, 5), clock);
        Assert.Equal(3, clock.Reads);

        reservation.Cancel(ReservationActor.Customer, null, Policy, clock);
        Assert.Equal(4, clock.Reads);

        var noShow = Confirmed();
        var late = new TickingClock(noShow.StartAtUtc);
        noShow.MarkNoShow(late);
        Assert.Equal(1, late.Reads);
        Assert.Equal(noShow.StartAtUtc, DomainAssert.Raised<ReservationNoShow>(noShow).OccurredAtUtc);
    }

    // ---- Confirm / Seat / Complete / NoShow ----------------------------------------------------------------------

    [Fact]
    public void Confirm_moves_a_pending_reservation_to_confirmed_and_raises_ReservationConfirmed()
    {
        var reservation = Pending();

        reservation.Confirm(_clock);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        var raised = DomainAssert.Raised<ReservationConfirmed>(reservation);
        Assert.Equal(new ReservationConfirmed(_tenantId, reservation.Id, _locationId, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Seated)]
    public void Confirm_rejects_a_reservation_that_is_not_pending(ReservationStatus from)
    {
        var reservation = InStatus(from);

        DomainAssert.Throws(ReservationErrors.NotPending, () => reservation.Confirm(_clock));

        Assert.Equal(from, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Seat_moves_a_confirmed_reservation_to_seated_and_raises_GuestSeated()
    {
        var reservation = Confirmed(partySize: 3);

        reservation.Seat(null, _clock);

        Assert.Equal(ReservationStatus.Seated, reservation.Status);
        var raised = DomainAssert.Raised<GuestSeated>(reservation);
        Assert.Equal(new GuestSeated(_tenantId, reservation.Id, _locationId, 3, null, _clock.UtcNow), raised);
        Assert.Null(reservation.TableSessionId);
    }

    [Fact]
    public void Seat_links_the_reservation_to_the_table_session_opened_for_the_party()
    {
        var reservation = Confirmed(partySize: 3);
        var sessionId = TableSessionId.New();

        reservation.Seat(sessionId, _clock);

        Assert.Equal(ReservationStatus.Seated, reservation.Status);
        Assert.Equal(sessionId, reservation.TableSessionId);
        Assert.Equal(sessionId, DomainAssert.Raised<GuestSeated>(reservation).TableSessionId);
    }

    [Fact]
    public void Seat_that_is_rejected_does_not_store_the_table_session()
    {
        var reservation = Pending();

        DomainAssert.Throws(ReservationErrors.NotConfirmed, () => reservation.Seat(TableSessionId.New(), _clock));

        Assert.Null(reservation.TableSessionId);
        DomainAssert.NoEvents(reservation);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Seated)]
    public void Seat_rejects_a_reservation_that_is_not_confirmed(ReservationStatus from)
    {
        var reservation = InStatus(from);

        DomainAssert.Throws(ReservationErrors.NotConfirmed, () => reservation.Seat(null, _clock));

        Assert.Equal(from, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Complete_moves_a_seated_reservation_to_completed_and_raises_ReservationCompleted()
    {
        var reservation = Seated();

        reservation.Complete(_clock);

        Assert.Equal(ReservationStatus.Completed, reservation.Status);
        var raised = DomainAssert.Raised<ReservationCompleted>(reservation);
        Assert.Equal(new ReservationCompleted(_tenantId, reservation.Id, _locationId, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    public void Complete_rejects_a_reservation_that_is_not_seated(ReservationStatus from)
    {
        var reservation = InStatus(from);

        DomainAssert.Throws(ReservationErrors.NotSeated, () => reservation.Complete(_clock));

        Assert.Equal(from, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void MarkNoShow_moves_a_confirmed_reservation_to_no_show_once_the_start_time_is_reached()
    {
        var customerId = CustomerId.New();
        var reservation = Create(customerId: customerId);
        reservation.Confirm(_clock);
        reservation.ClearDomainEvents();
        _clock.Set(reservation.StartAtUtc);

        reservation.MarkNoShow(_clock);

        Assert.Equal(ReservationStatus.NoShow, reservation.Status);
        var raised = DomainAssert.Raised<ReservationNoShow>(reservation);
        Assert.Equal(new ReservationNoShow(_tenantId, reservation.Id, _locationId, customerId, _clock.UtcNow), raised);
    }

    [Fact]
    public void MarkNoShow_rejects_a_reservation_whose_start_time_is_not_reached()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddSeconds(-1));

        DomainAssert.Throws(ReservationErrors.NoShowBeforeStart, () => reservation.MarkNoShow(_clock));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Seated)]
    public void MarkNoShow_rejects_a_reservation_that_is_not_confirmed(ReservationStatus from)
    {
        var reservation = InStatus(from);
        _clock.Set(reservation.StartAtUtc.AddMinutes(30));

        DomainAssert.Throws(ReservationErrors.NotConfirmed, () => reservation.MarkNoShow(_clock));

        Assert.Equal(from, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    // ---- Cancel --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    public void Cancel_by_the_customer_before_the_cutoff_cancels_and_raises_ReservationCancelled(ReservationStatus from)
    {
        var reservation = InStatus(from);

        reservation.Cancel(ReservationActor.Customer, "  Empêchement ", Policy, _clock);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(ReservationActor.Customer, reservation.CancelledBy);
        Assert.Equal("Empêchement", reservation.CancellationReason);
        Assert.False(reservation.HoldsCapacity);
        var raised = DomainAssert.Raised<ReservationCancelled>(reservation);
        Assert.Equal(
            new ReservationCancelled(_tenantId, reservation.Id, _locationId, ReservationActor.Customer, "Empêchement", _clock.UtcNow),
            raised);
    }

    [Fact]
    public void Cancel_by_the_customer_is_accepted_exactly_at_the_cutoff()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddHours(-2));

        reservation.Cancel(ReservationActor.Customer, null, Policy, _clock);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Null(reservation.CancellationReason);
    }

    [Fact]
    public void Cancel_by_the_customer_is_refused_after_the_cutoff()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddHours(-2).AddSeconds(1));

        DomainAssert.Throws(
            ReservationErrors.CancellationCutoffPassed,
            () => reservation.Cancel(ReservationActor.Customer, null, Policy, _clock));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.True(reservation.HoldsCapacity);
        Assert.Null(reservation.CancelledBy);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Cancel_by_staff_overrides_the_cutoff()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddMinutes(-10));

        reservation.Cancel(ReservationActor.Staff, "Fermeture exceptionnelle", Policy, _clock);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(ReservationActor.Staff, reservation.CancelledBy);
        Assert.Equal(ReservationActor.Staff, DomainAssert.Raised<ReservationCancelled>(reservation).CancelledBy);
    }

    [Fact]
    public void Cancel_by_staff_is_still_possible_after_the_start_time_while_not_seated()
    {
        var reservation = Pending();
        _clock.Set(reservation.StartAtUtc.AddMinutes(45));

        reservation.Cancel(ReservationActor.Staff, null, Policy, _clock);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_by_the_system_overrides_the_cutoff_like_staff()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddMinutes(-10));

        reservation.Cancel(ReservationActor.System, "Fermeture de l'établissement", Policy, _clock);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(ReservationActor.System, reservation.CancelledBy);
        Assert.Equal(ReservationActor.System, DomainAssert.Raised<ReservationCancelled>(reservation).CancelledBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Cancel_rejects_an_undefined_actor(int actor)
    {
        var reservation = Confirmed();

        DomainAssert.Throws(
            ReservationErrors.ActorInvalid,
            () => reservation.Cancel((ReservationActor)actor, null, Policy, _clock));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.CancelledBy);
        DomainAssert.NoEvents(reservation);
    }

    [Theory]
    [InlineData(ReservationActor.Customer)]
    [InlineData(ReservationActor.Staff)]
    [InlineData(ReservationActor.System)]
    public void Cancel_rejects_a_seated_reservation(ReservationActor actor)
    {
        var reservation = Seated();

        DomainAssert.Throws(
            ReservationErrors.NotCancellableAfterSeating,
            () => reservation.Cancel(actor, null, Policy, _clock));

        Assert.Equal(ReservationStatus.Seated, reservation.Status);
        DomainAssert.NoEvents(reservation);
    }

    // ---- États terminaux -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public void Terminal_reservation_rejects_every_transition_and_change(ReservationStatus terminal)
    {
        var reservation = InStatus(terminal);
        _clock.Set(FakeClock.DefaultNow);
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var move = Decide(newSlot, reservation.PartySize, excluded: reservation.Id);
        var resize = Decide(reservation.Slot, 2, excluded: reservation.Id);

        DomainAssert.Throws(ReservationErrors.AlreadyTerminal, () => reservation.Confirm(_clock));
        DomainAssert.Throws(ReservationErrors.AlreadyTerminal, () => reservation.Seat(null, _clock));
        DomainAssert.Throws(ReservationErrors.AlreadyTerminal, () => reservation.Complete(_clock));
        DomainAssert.Throws(ReservationErrors.AlreadyTerminal, () => reservation.MarkNoShow(_clock));
        DomainAssert.Throws(ReservationErrors.AlreadyTerminal, () => reservation.Cancel(ReservationActor.Staff, null, Policy, _clock));
        DomainAssert.Throws(
            ReservationErrors.AlreadyTerminal,
            () => reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, move, _clock));
        DomainAssert.Throws(
            ReservationErrors.AlreadyTerminal,
            () => reservation.ChangePartySize(2, ReservationActor.Staff, Policy, resize, _clock));

        Assert.Equal(terminal, reservation.Status);
        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    // ---- Reschedule ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    public void Reschedule_moves_the_slot_keeps_the_status_and_raises_ReservationRescheduled(ReservationStatus from)
    {
        var reservation = InStatus(from);
        var newStart = Tomorrow8Pm.AddDays(2);
        var newSlot = Period.Create(newStart, newStart.AddHours(3));

        reservation.Reschedule(
            newSlot, ReservationActor.Customer, Policy, Decide(newSlot, 4, excluded: reservation.Id), _clock);

        Assert.Equal(newStart, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.FromHours(3), reservation.Duration);
        Assert.Equal(from, reservation.Status);
        var raised = DomainAssert.Raised<ReservationRescheduled>(reservation);
        Assert.Equal(
            new ReservationRescheduled(
                _tenantId, reservation.Id, _locationId, Tomorrow8Pm, TimeSpan.FromHours(2), newStart, TimeSpan.FromHours(3), _clock.UtcNow),
            raised);
    }

    [Fact]
    public void Reschedule_to_the_same_slot_raises_nothing_even_inside_the_lead_time()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddMinutes(-20));
        var sameSlot = SlotAt(Tomorrow8Pm);

        reservation.Reschedule(
            sameSlot, ReservationActor.Staff, Policy, Decide(sameSlot, 4, excluded: reservation.Id), _clock);

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.FromHours(2), reservation.Duration);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_to_the_same_slot_is_still_rejected_for_a_customer_after_the_cutoff_or_once_terminal()
    {
        var late = Confirmed();
        var cancelled = InStatus(ReservationStatus.Cancelled);
        _clock.Set(late.StartAtUtc.AddHours(-1));
        var sameSlot = SlotAt(Tomorrow8Pm);
        var decision = Decide(sameSlot, 4);

        DomainAssert.Throws(
            ReservationErrors.ModificationCutoffPassed,
            () => late.Reschedule(sameSlot, ReservationActor.Customer, Policy, decision, _clock));
        DomainAssert.Throws(
            ReservationErrors.AlreadyTerminal,
            () => cancelled.Reschedule(sameSlot, ReservationActor.Staff, Policy, decision, _clock));
    }

    [Fact]
    public void Reschedule_is_forbidden_once_the_guests_are_seated()
    {
        var reservation = Seated();
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));

        DomainAssert.Throws(
            ReservationErrors.NotModifiableAfterSeating,
            () => reservation.Reschedule(
                newSlot, ReservationActor.Staff, Policy, Decide(newSlot, 4, excluded: reservation.Id), _clock));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_rejects_a_new_slot_outside_the_booking_window()
    {
        var reservation = Confirmed();
        var tooSoon = SlotAt(_clock.UtcNow.AddMinutes(30));
        var tooFar = SlotAt(_clock.UtcNow.AddDays(31));

        DomainAssert.Throws(
            ReservationErrors.LeadTimeNotRespected,
            () => reservation.Reschedule(
                tooSoon, ReservationActor.Staff, Policy, Decide(tooSoon, 4, excluded: reservation.Id), _clock));
        DomainAssert.Throws(
            ReservationErrors.BeyondBookingHorizon,
            () => reservation.Reschedule(
                tooFar, ReservationActor.Staff, Policy, Decide(tooFar, 4, excluded: reservation.Id), _clock));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_rejects_a_new_slot_without_enough_capacity()
    {
        var reservation = Confirmed(partySize: 4);
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var others = new[] { ExistingBooking.Create(ReservationId.New(), newSlot, 8) };
        var refused = Decide(newSlot, 4, capacity: 10, existing: others, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.CapacityInsufficient,
            () => reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, refused, _clock));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_does_not_count_the_reservation_own_seats_against_itself()
    {
        var reservation = Confirmed(partySize: 4);
        var overlappingSlot = SlotAt(Tomorrow8Pm.AddHours(1));
        var state = new[] { reservation.ToExistingBooking() };
        var withoutExclusion = Decide(overlappingSlot, 4, capacity: 4, existing: state);
        var withExclusion = Decide(overlappingSlot, 4, capacity: 4, existing: state, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.CapacityInsufficient,
            () => reservation.Reschedule(overlappingSlot, ReservationActor.Staff, Policy, withoutExclusion, _clock));

        reservation.Reschedule(overlappingSlot, ReservationActor.Staff, Policy, withExclusion, _clock);

        Assert.Equal(Tomorrow8Pm.AddHours(1), reservation.StartAtUtc);
    }

    [Fact]
    public void Reschedule_rejects_a_decision_made_for_another_request()
    {
        var reservation = Confirmed(partySize: 4);
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var forCurrentSlot = Decide(reservation.Slot, 4, excluded: reservation.Id);
        var forAnotherSize = Decide(newSlot, 2, excluded: reservation.Id);
        var excludingSomeoneElse = Decide(newSlot, 4, excluded: ReservationId.New());

        foreach (var wrong in new[] { forCurrentSlot, forAnotherSize, excludingSomeoneElse })
        {
            DomainAssert.Throws(
                ReservationErrors.AvailabilityDecisionMismatch,
                () => reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, wrong, _clock));
        }

        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Reschedule_by_the_customer_is_refused_after_the_cutoff_but_staff_can_override()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddHours(-1));
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var decision = Decide(newSlot, 4, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.ModificationCutoffPassed,
            () => reservation.Reschedule(newSlot, ReservationActor.Customer, Policy, decision, _clock));
        DomainAssert.NoEvents(reservation);

        reservation.Reschedule(newSlot, ReservationActor.Staff, Policy, decision, _clock);

        Assert.Equal(newSlot.StartUtc, reservation.StartAtUtc);
    }

    [Fact]
    public void Reschedule_and_ChangePartySize_by_the_system_override_the_cutoff_like_staff()
    {
        var reservation = Confirmed();
        _clock.Set(reservation.StartAtUtc.AddHours(-1));
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));

        reservation.Reschedule(newSlot, ReservationActor.System, Policy, Decide(newSlot, 4, excluded: reservation.Id), _clock);
        _clock.Set(reservation.StartAtUtc.AddHours(-1));
        reservation.ChangePartySize(
            5, ReservationActor.System, Policy, Decide(reservation.Slot, 5, excluded: reservation.Id), _clock);

        Assert.Equal(newSlot.StartUtc, reservation.StartAtUtc);
        Assert.Equal(5, reservation.PartySize);
    }

    [Fact]
    public void Reschedule_and_ChangePartySize_reject_an_undefined_actor()
    {
        var reservation = Confirmed();
        var newSlot = SlotAt(Tomorrow8Pm.AddDays(1));
        var move = Decide(newSlot, 4, excluded: reservation.Id);
        var resize = Decide(reservation.Slot, 5, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.ActorInvalid,
            () => reservation.Reschedule(newSlot, (ReservationActor)0, Policy, move, _clock));
        DomainAssert.Throws(
            ReservationErrors.ActorInvalid,
            () => reservation.ChangePartySize(5, (ReservationActor)99, Policy, resize, _clock));

        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    // ---- ChangePartySize -----------------------------------------------------------------------------------------

    [Fact]
    public void ChangePartySize_updates_the_size_and_raises_ReservationPartySizeChanged()
    {
        var reservation = Confirmed(partySize: 4);

        reservation.ChangePartySize(
            6, ReservationActor.Customer, Policy, Decide(reservation.Slot, 6, excluded: reservation.Id), _clock);

        Assert.Equal(6, reservation.PartySize);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        var raised = DomainAssert.Raised<ReservationPartySizeChanged>(reservation);
        Assert.Equal(new ReservationPartySizeChanged(_tenantId, reservation.Id, _locationId, 4, 6, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChangePartySize_rejects_a_size_that_is_not_positive(int partySize)
    {
        var reservation = Confirmed(partySize: 4);
        var decision = Decide(reservation.Slot, 4, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.PartySizeNotPositive,
            () => reservation.ChangePartySize(partySize, ReservationActor.Staff, Policy, decision, _clock));

        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_rejects_a_growth_the_slot_cannot_absorb()
    {
        var reservation = Confirmed(partySize: 4);
        var state = new[]
        {
            reservation.ToExistingBooking(),
            ExistingBooking.Create(ReservationId.New(), reservation.Slot, 6),
        };
        var toFive = Decide(reservation.Slot, 5, capacity: 10, existing: state, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.CapacityInsufficient,
            () => reservation.ChangePartySize(5, ReservationActor.Staff, Policy, toFive, _clock));

        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_accepts_a_reduction_on_a_full_slot()
    {
        var reservation = Confirmed(partySize: 4);
        var state = new[]
        {
            reservation.ToExistingBooking(),
            ExistingBooking.Create(ReservationId.New(), reservation.Slot, 6),
        };

        reservation.ChangePartySize(
            2,
            ReservationActor.Customer,
            Policy,
            Decide(reservation.Slot, 2, capacity: 10, existing: state, excluded: reservation.Id),
            _clock);

        Assert.Equal(2, reservation.PartySize);
    }

    [Fact]
    public void ChangePartySize_rejects_a_decision_made_for_another_request()
    {
        var reservation = Confirmed(partySize: 4);
        var forAnotherSize = Decide(reservation.Slot, 5, excluded: reservation.Id);
        var forAnotherSlot = Decide(SlotAt(Tomorrow8Pm.AddDays(1)), 6, excluded: reservation.Id);
        var excludingSomeoneElse = Decide(reservation.Slot, 6, excluded: ReservationId.New());

        foreach (var wrong in new[] { forAnotherSize, forAnotherSlot, excludingSomeoneElse })
        {
            DomainAssert.Throws(
                ReservationErrors.AvailabilityDecisionMismatch,
                () => reservation.ChangePartySize(6, ReservationActor.Staff, Policy, wrong, _clock));
        }

        Assert.Equal(4, reservation.PartySize);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_to_the_same_size_raises_nothing()
    {
        var reservation = Confirmed(partySize: 4);

        reservation.ChangePartySize(
            4, ReservationActor.Staff, Policy, Decide(reservation.Slot, 4, excluded: reservation.Id), _clock);

        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_is_forbidden_once_the_guests_are_seated()
    {
        var reservation = Seated();

        DomainAssert.Throws(
            ReservationErrors.NotModifiableAfterSeating,
            () => reservation.ChangePartySize(
                2, ReservationActor.Staff, Policy, Decide(reservation.Slot, 2, excluded: reservation.Id), _clock));
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void ChangePartySize_by_the_customer_is_refused_after_the_cutoff_but_staff_can_override()
    {
        var reservation = Confirmed(partySize: 4);
        _clock.Set(reservation.StartAtUtc.AddMinutes(-30));
        var decision = Decide(reservation.Slot, 5, excluded: reservation.Id);

        DomainAssert.Throws(
            ReservationErrors.ModificationCutoffPassed,
            () => reservation.ChangePartySize(5, ReservationActor.Customer, Policy, decision, _clock));
        DomainAssert.NoEvents(reservation);

        reservation.ChangePartySize(5, ReservationActor.Staff, Policy, decision, _clock);

        Assert.Equal(5, reservation.PartySize);
    }

    // ---- Réservation de capacité ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReservationStatus.Pending, true)]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Seated, true)]
    [InlineData(ReservationStatus.Completed, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    [InlineData(ReservationStatus.NoShow, false)]
    public void Only_pending_confirmed_and_seated_reservations_hold_capacity(ReservationStatus status, bool holds)
    {
        var reservation = InStatus(status);

        Assert.Equal(holds, reservation.HoldsCapacity);
        Assert.Equal(holds, status.HoldsCapacity());
        Assert.Equal(!holds, status.IsTerminal());
        Assert.Equal(holds, ReservationStatusExtensions.CapacityHoldingStatuses.Contains(status));
    }

    // ---- AnonymizeGuest ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Seated)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public void AnonymizeGuest_erases_the_contact_in_every_status_and_raises_ReservationGuestAnonymized(ReservationStatus status)
    {
        var reservation = InStatus(status);
        Assert.False(reservation.IsGuestAnonymized);

        reservation.AnonymizeGuest(_clock);

        Assert.True(reservation.IsGuestAnonymized);
        Assert.Equal(GuestContact.Anonymized, reservation.GuestContact);
        Assert.Null(reservation.GuestContact.Phone);
        Assert.Null(reservation.GuestContact.Email);
        Assert.NotEqual(Contact.Name, reservation.GuestContact.Name);
        Assert.Equal(status, reservation.Status);
        Assert.Equal(
            new ReservationGuestAnonymized(_tenantId, reservation.Id, _locationId, null, _clock.UtcNow),
            DomainAssert.Raised<ReservationGuestAnonymized>(reservation));
    }

    [Fact]
    public void AnonymizeGuest_clears_the_notes_and_keeps_the_customer_id_and_the_booking_facts()
    {
        var customerId = CustomerId.New();
        var reservation = Create(partySize: 4, notes: "Allergie aux arachides", customerId: customerId);
        reservation.ClearDomainEvents();

        reservation.AnonymizeGuest(_clock);

        Assert.Null(reservation.Notes);
        Assert.Equal(customerId, reservation.CustomerId);
        Assert.Equal(4, reservation.PartySize);
        Assert.Equal(Tomorrow8Pm, reservation.StartAtUtc);
        Assert.True(reservation.HoldsCapacity);
        Assert.Equal(customerId, DomainAssert.Raised<ReservationGuestAnonymized>(reservation).CustomerId);
    }

    [Fact]
    public void AnonymizeGuest_twice_is_a_no_op_without_event()
    {
        var reservation = Confirmed();
        reservation.AnonymizeGuest(_clock);
        reservation.ClearDomainEvents();

        reservation.AnonymizeGuest(_clock);

        Assert.True(reservation.IsGuestAnonymized);
        DomainAssert.NoEvents(reservation);
    }

    [Fact]
    public void Anonymized_reservation_still_follows_its_lifecycle()
    {
        var reservation = Confirmed();
        reservation.AnonymizeGuest(_clock);

        reservation.Seat(null, _clock);
        reservation.Complete(_clock);

        Assert.Equal(ReservationStatus.Completed, reservation.Status);
        Assert.True(reservation.IsGuestAnonymized);
    }

    [Fact]
    public void ToExistingBooking_exposes_the_slot_and_seats_of_a_reservation_holding_capacity()
    {
        var reservation = Confirmed(partySize: 4);

        var booking = reservation.ToExistingBooking();

        Assert.Equal(ExistingBooking.Create(reservation.Id, SlotAt(Tomorrow8Pm), 4), booking);
    }

    [Fact]
    public void ToExistingBooking_rejects_a_reservation_that_no_longer_holds_capacity()
    {
        var reservation = InStatus(ReservationStatus.Cancelled);

        DomainAssert.Throws(ReservationErrors.DoesNotHoldCapacity, () => reservation.ToExistingBooking());
    }

    [Fact]
    public void ReservationId_rejects_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ReservationId.From(Guid.Empty));
    }
}

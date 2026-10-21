namespace Reservations.Domain;

public sealed class ReservationAvailabilityPolicyTests
{
    private static readonly DateTimeOffset EightPm = new(2026, 1, 16, 20, 0, 0, TimeSpan.Zero);
    private static readonly Period Slot = Period.Create(EightPm, EightPm.AddHours(2));
    private static readonly ReservationPolicy Policy = ReservationPolicy.Default;
    private static readonly GuestContact Contact = GuestContact.Create("Koffi", null, Email.Create("koffi@example.com"));

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();

    private static ExistingBooking Booking(int partySize, double startOffsetHours = 0, double durationHours = 2) =>
        ExistingBooking.Create(
            ReservationId.New(),
            Period.Create(EightPm.AddHours(startOffsetHours), EightPm.AddHours(startOffsetHours + durationHours)),
            partySize);

    private AvailabilityDecision Evaluate(int partySize, int capacity, params ExistingBooking[] existing) =>
        ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, partySize, capacity, existing);

    private Reservation Book(int partySize, AvailabilityDecision decision) =>
        Reservation.Create(
            _tenantId, new LocationReference(_tenantId, _locationId, IsOperational: true), null, Contact, partySize, Slot, null, Policy, decision, _clock);

    [Fact]
    public void Evaluate_accepts_a_party_on_an_empty_slot_and_describes_the_request()
    {
        var decision = Evaluate(partySize: 4, capacity: 10);

        Assert.True(decision.IsAvailable);
        Assert.Equal(_locationId, decision.LocationId);
        Assert.Equal(Slot, decision.Slot);
        Assert.Equal(4, decision.PartySize);
        Assert.Equal(10, decision.Capacity);
        Assert.Equal(0, decision.PeakBookedSeats);
        Assert.Equal(10, decision.RemainingSeats);
        Assert.Null(decision.ExcludedReservationId);
    }

    [Fact]
    public void Evaluate_accepts_a_party_that_fills_the_capacity_exactly()
    {
        var decision = Evaluate(partySize: 4, capacity: 10, Booking(6));

        Assert.True(decision.IsAvailable);
        Assert.Equal(6, decision.PeakBookedSeats);
        Assert.Equal(4, decision.RemainingSeats);
    }

    [Fact]
    public void Evaluate_refuses_a_party_that_would_exceed_the_capacity_by_one_seat()
    {
        var decision = Evaluate(partySize: 5, capacity: 10, Booking(6));

        Assert.False(decision.IsAvailable);
        Assert.Equal(4, decision.RemainingSeats);
    }

    [Fact]
    public void Evaluate_refuses_everything_when_the_capacity_is_zero()
    {
        Assert.False(Evaluate(partySize: 1, capacity: 0).IsAvailable);
    }

    [Fact]
    public void Evaluate_reports_zero_remaining_seats_when_the_slot_is_already_overbooked()
    {
        var decision = Evaluate(partySize: 1, capacity: 4, Booking(6));

        Assert.False(decision.IsAvailable);
        Assert.Equal(0, decision.RemainingSeats);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Evaluate_rejects_a_party_size_that_is_not_positive(int partySize)
    {
        DomainAssert.Throws(ReservationErrors.PartySizeNotPositive, () => Evaluate(partySize, capacity: 10));
    }

    [Fact]
    public void Evaluate_rejects_a_negative_capacity()
    {
        DomainAssert.Throws(ReservationErrors.CapacityNegative, () => Evaluate(partySize: 2, capacity: -1));
    }

    [Fact]
    public void Evaluate_ignores_bookings_that_do_not_overlap_the_slot()
    {
        var endsWhenSlotStarts = Booking(10, startOffsetHours: -2);
        var startsWhenSlotEnds = Booking(10, startOffsetHours: 2);
        var anotherDay = Booking(10, startOffsetHours: 24);

        var decision = Evaluate(partySize: 10, capacity: 10, endsWhenSlotStarts, startsWhenSlotEnds, anotherDay);

        Assert.True(decision.IsAvailable);
        Assert.Equal(0, decision.PeakBookedSeats);
    }

    [Fact]
    public void Evaluate_counts_a_booking_that_overlaps_the_slot_only_partially()
    {
        var endsInsideSlot = Booking(6, startOffsetHours: -1.5);
        var startsInsideSlot = Booking(6, startOffsetHours: 1.75);

        Assert.False(Evaluate(partySize: 5, capacity: 10, endsInsideSlot).IsAvailable);
        Assert.False(Evaluate(partySize: 5, capacity: 10, startsInsideSlot).IsAvailable);
    }

    [Fact]
    public void Evaluate_does_not_add_up_back_to_back_bookings_inside_the_slot()
    {
        var firstHour = Booking(6, startOffsetHours: 0, durationHours: 1);
        var secondHour = Booking(6, startOffsetHours: 1, durationHours: 1);

        var decision = Evaluate(partySize: 4, capacity: 10, firstHour, secondHour);

        Assert.Equal(6, decision.PeakBookedSeats);
        Assert.True(decision.IsAvailable);
    }

    [Fact]
    public void Evaluate_adds_up_bookings_present_at_the_same_instant()
    {
        var early = Booking(6, startOffsetHours: -1, durationHours: 2);
        var late = Booking(3, startOffsetHours: 0.5, durationHours: 2);

        var decision = Evaluate(partySize: 2, capacity: 10, early, late);

        Assert.Equal(9, decision.PeakBookedSeats);
        Assert.False(decision.IsAvailable);
        Assert.True(Evaluate(partySize: 1, capacity: 10, early, late).IsAvailable);
    }

    [Fact]
    public void Evaluate_leaves_out_the_excluded_reservation_only()
    {
        var own = Booking(4);
        var other = Booking(6);

        var decision = ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 4, 10, [own, other], own.ReservationId);

        Assert.Equal(own.ReservationId, decision.ExcludedReservationId);
        Assert.Equal(6, decision.PeakBookedSeats);
        Assert.True(decision.IsAvailable);
    }

    [Fact]
    public void Last_seats_requested_twice_on_the_same_state_only_the_first_one_gets_them()
    {
        const int capacity = 10;
        var state = new List<ExistingBooking> { Booking(6) };

        // Les deux requêtes voient 4 places restantes sur le même état...
        var first = Book(4, ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 4, capacity, state));
        Assert.Equal(ReservationStatus.Pending, first.Status);

        // ...mais une fois la première comptée, la seconde est réévaluée et refusée.
        state.Add(first.ToExistingBooking());
        var second = ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 4, capacity, state);

        Assert.False(second.IsAvailable);
        Assert.Equal(0, second.RemainingSeats);
        DomainAssert.Throws(ReservationErrors.CapacityInsufficient, () => Book(4, second));
        DomainAssert.Throws(
            ReservationErrors.CapacityInsufficient,
            () => Book(1, ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 1, capacity, state)));
    }

    [Fact]
    public void Seats_of_a_cancelled_reservation_become_available_again()
    {
        const int capacity = 10;
        var others = Booking(6);
        var first = Book(4, Evaluate(4, capacity, others));
        Assert.False(Evaluate(4, capacity, others, first.ToExistingBooking()).IsAvailable);

        first.Cancel(ReservationActor.Customer, null, Policy, _clock);

        // Une réservation annulée ne retient plus de capacité, elle ne fait donc plus partie de l'état.
        Assert.False(first.HoldsCapacity);
        var second = Book(4, Evaluate(4, capacity, others));
        Assert.Equal(ReservationStatus.Pending, second.Status);
    }

    [Fact]
    public void A_decision_cannot_be_reused_for_a_bigger_party()
    {
        var decisionForTwo = Evaluate(partySize: 2, capacity: 10, Booking(6));

        DomainAssert.Throws(ReservationErrors.AvailabilityDecisionMismatch, () => Book(5, decisionForTwo));
    }

    [Fact]
    public void Decisions_for_the_same_request_and_state_are_distinct_single_use_tokens()
    {
        var booking = Booking(6);
        var first = Evaluate(4, 10, booking);
        var second = Evaluate(4, 10, booking);

        // Ce ne sont plus des value objects : chaque évaluation est un jeton à part, consommé indépendamment.
        Assert.NotEqual(first, second);
        Book(4, first);
        Assert.True(first.IsConsumed);
        Assert.False(second.IsConsumed);
    }

    [Fact]
    public void A_decision_cannot_be_replayed_to_book_the_last_seats_twice()
    {
        var lastSeats = Evaluate(partySize: 4, capacity: 10, Booking(6));

        Book(4, lastSeats);

        DomainAssert.Throws(ReservationErrors.AvailabilityDecisionAlreadyUsed, () => Book(4, lastSeats));
    }

    [Fact]
    public void Evaluate_does_not_let_an_int_MaxValue_party_wrap_around_the_capacity()
    {
        var decision = Evaluate(partySize: int.MaxValue, capacity: 10, Booking(6));

        Assert.False(decision.IsAvailable);
        Assert.Equal(6, decision.PeakBookedSeats);
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => Book(int.MaxValue, decision));
    }

    [Fact]
    public void Evaluate_does_not_let_many_large_bookings_wrap_around_and_vanish()
    {
        // 500 000 réservations de 10 000 convives : 5 milliards de places, qui deviennent négatives en arithmétique int.
        var crowd = Enumerable.Repeat(ExistingBooking.ForWalkIn(Slot, ExistingBooking.MaxPartySize), 500_000).ToArray();

        var decision = Evaluate(partySize: 1, capacity: ReservationAvailabilityPolicy.MaxCapacity, crowd);

        Assert.Equal(int.MaxValue, decision.PeakBookedSeats);
        Assert.Equal(0, decision.RemainingSeats);
        Assert.False(decision.IsAvailable);
    }

    [Fact]
    public void Bookings_and_capacity_are_bounded_to_a_sane_maximum()
    {
        Assert.Equal(ExistingBooking.MaxPartySize, ExistingBooking.ForWalkIn(Slot, ExistingBooking.MaxPartySize).PartySize);
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => ExistingBooking.ForWalkIn(Slot, ExistingBooking.MaxPartySize + 1));
        DomainAssert.Throws(ReservationErrors.PartySizeTooLarge, () => ExistingBooking.ForWalkIn(Slot, int.MaxValue));
        DomainAssert.Throws(
            ReservationErrors.PartySizeTooLarge,
            () => ExistingBooking.Create(ReservationId.New(), Slot, int.MaxValue));

        Assert.Equal(
            ReservationAvailabilityPolicy.MaxCapacity,
            Evaluate(1, ReservationAvailabilityPolicy.MaxCapacity).Capacity);
        DomainAssert.Throws(
            ReservationErrors.CapacityTooLarge,
            () => Evaluate(1, ReservationAvailabilityPolicy.MaxCapacity + 1));
        DomainAssert.Throws(ReservationErrors.CapacityTooLarge, () => Evaluate(1, int.MaxValue));
    }

    [Fact]
    public void Evaluate_and_ExistingBooking_reject_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ReservationAvailabilityPolicy.Evaluate(default, Slot, 2, 10, []));
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 2, 10, [], default(ReservationId)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => ExistingBooking.Create(default, Slot, 2));
    }

    [Fact]
    public void Evaluate_treats_a_null_booking_as_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => Evaluate(2, 10, Booking(2), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExistingBooking_rejects_a_party_size_that_is_not_positive(int partySize)
    {
        DomainAssert.Throws(
            ReservationErrors.PartySizeNotPositive,
            () => ExistingBooking.Create(ReservationId.New(), Slot, partySize));
        DomainAssert.Throws(
            ReservationErrors.PartySizeNotPositive,
            () => ExistingBooking.ForWalkIn(Slot, partySize));
    }

    // ---- Walk-ins ----

    [Fact]
    public void ForWalkIn_builds_an_occupancy_without_reservation_id()
    {
        var walkIn = ExistingBooking.ForWalkIn(Slot, 3);

        Assert.True(walkIn.IsWalkIn);
        Assert.Null(walkIn.ReservationId);
        Assert.Equal(Slot, walkIn.Slot);
        Assert.Equal(3, walkIn.PartySize);
        Assert.False(Booking(3).IsWalkIn);
    }

    [Fact]
    public void Evaluate_counts_walk_ins_against_the_capacity()
    {
        var withoutWalkIn = Evaluate(partySize: 4, capacity: 10, Booking(4));
        var withWalkIn = Evaluate(partySize: 4, capacity: 10, Booking(4), ExistingBooking.ForWalkIn(Slot, 3));

        Assert.True(withoutWalkIn.IsAvailable);
        Assert.Equal(6, withoutWalkIn.RemainingSeats);
        Assert.False(withWalkIn.IsAvailable);
        Assert.Equal(7, withWalkIn.PeakBookedSeats);
        Assert.Equal(3, withWalkIn.RemainingSeats);
        DomainAssert.Throws(ReservationErrors.CapacityInsufficient, () => Book(4, withWalkIn));
    }

    [Fact]
    public void Evaluate_adds_up_several_walk_ins_even_when_they_are_identical()
    {
        var decision = Evaluate(
            partySize: 2, capacity: 10, ExistingBooking.ForWalkIn(Slot, 4), ExistingBooking.ForWalkIn(Slot, 4));

        Assert.Equal(8, decision.PeakBookedSeats);
        Assert.True(decision.IsAvailable);
    }

    [Fact]
    public void Evaluate_ignores_a_walk_in_that_has_left_before_the_slot()
    {
        var earlier = ExistingBooking.ForWalkIn(Period.Create(EightPm.AddHours(-2), EightPm), 10);

        Assert.True(Evaluate(partySize: 10, capacity: 10, earlier).IsAvailable);
    }

    [Fact]
    public void Evaluate_never_leaves_a_walk_in_out_when_a_reservation_is_excluded()
    {
        var own = Booking(4);
        var walkIn = ExistingBooking.ForWalkIn(Slot, 6);

        var decision = ReservationAvailabilityPolicy.Evaluate(_locationId, Slot, 5, 10, [own, walkIn], own.ReservationId);

        Assert.Equal(6, decision.PeakBookedSeats);
        Assert.False(decision.IsAvailable);
    }
}

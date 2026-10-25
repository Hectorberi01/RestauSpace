using Reservations.Application.Commands;
using Reservations.Application.Queries;
using Reservations.Domain;

namespace Reservations.Application;

public sealed class ReservationUseCaseTests
{
    private readonly ReservationsFixture _fixture = new();

    [Fact]
    public async Task A_reservation_is_taken_pending_under_the_capacity_lock_with_the_default_duration()
    {
        var id = await _fixture.Create.HandleAsync(_fixture.NewReservation());

        var reservation = _fixture.Reservations.Stored(id);
        Assert.Equal(_fixture.TenantA, reservation.TenantId);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal(_fixture.Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.FromHours(2), reservation.Duration);
        Assert.Equal("Awa Dossou", reservation.GuestContact.Name);
        Assert.Null(reservation.CustomerId);
        Assert.Equal(1, _fixture.LocksTaken);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<ReservationCreated>(reservation);
    }

    [Fact]
    public async Task A_start_given_with_an_offset_is_stored_as_the_same_instant_in_utc()
    {
        var local = new DateTimeOffset(2026, 1, 16, 21, 0, 0, TimeSpan.FromHours(1));

        var id = await _fixture.Create.HandleAsync(_fixture.NewReservation(startAt: local) with { DurationMinutes = 90 });

        var reservation = _fixture.Reservations.Stored(id);
        Assert.Equal(TimeSpan.Zero, reservation.StartAtUtc.Offset);
        Assert.Equal(_fixture.Tomorrow8Pm, reservation.StartAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(90), reservation.Duration);
    }

    [Fact]
    public async Task The_last_seats_go_to_the_first_request_and_walk_ins_count()
    {
        await _fixture.GivenReservationAsync(partySize: 4);
        // Deux clients sans réservation installés maintenant : ils occupent leurs places jusqu'à 14:00, pas à 20:00.
        _fixture.GivenOpenSession(2);
        // Une session ouverte pour une réservation compte déjà par la réservation.
        _fixture.GivenOpenSession(6, forReservation: true, openedAt: _fixture.Tomorrow8Pm.AddDays(-2));

        await _fixture.GivenReservationAsync(partySize: 6);
        var full = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 1)));

        Assert.Equal(ReservationErrors.CapacityInsufficient, full.Error);
        Assert.Equal(2, _fixture.Reservations.Items.Count);
        // À 13:00, les deux clients sans réservation occupent encore leurs places.
        var soon = await _fixture.Availability.HandleAsync(new GetAvailabilityQuery(_fixture.Location, _fixture.Clock.UtcNow.AddHours(1), 9));
        Assert.Equal(2, soon.PeakBookedSeats);
        Assert.False(soon.IsAvailable);
    }

    [Fact]
    public async Task Availability_reports_capacity_opening_hours_and_takes_no_lock()
    {
        await _fixture.GivenReservationAsync(partySize: 4);

        var available = await _fixture.Availability.HandleAsync(new GetAvailabilityQuery(_fixture.Location, _fixture.Tomorrow8Pm, 6));
        var tooMany = await _fixture.Availability.HandleAsync(new GetAvailabilityQuery(_fixture.Location, _fixture.Tomorrow8Pm, 7));
        _fixture.Closed.Add(LocationId.From(_fixture.Location));
        var closed = await _fixture.Availability.HandleAsync(new GetAvailabilityQuery(_fixture.Location, _fixture.Tomorrow8Pm, 2));

        Assert.True(available.IsAvailable);
        Assert.Equal((10, 4, 6), (available.Capacity, available.PeakBookedSeats, available.RemainingSeats));
        Assert.Equal(120, available.DurationMinutes);
        Assert.False(tooMany.IsAvailable);
        Assert.False(closed.OpenDuringSlot);
        Assert.False(closed.IsAvailable);
        Assert.Equal(0, _fixture.LocksTaken);
    }

    [Fact]
    public async Task A_slot_outside_opening_hours_or_a_location_of_another_tenant_is_refused()
    {
        var ofB = _fixture.GivenLocation(_fixture.TenantB);
        _fixture.Closed.Add(LocationId.From(_fixture.Location));

        var closed = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation()));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Create.HandleAsync(_fixture.NewReservation(locationId: ofB)));

        Assert.Equal(ReservationsApplicationErrors.LocationClosedDuringSlot, closed.Error);
        Assert.Equal(ReservationsApplicationErrors.LocationNotFound, foreign.Error);
        Assert.Empty(_fixture.Reservations.Items);
        Assert.Equal(0, _fixture.LocksTaken);
    }

    [Fact]
    public async Task A_frozen_location_takes_no_new_reservation_from_the_staff_nor_online()
    {
        _fixture.Frozen.Add(LocationId.From(_fixture.Location));

        var staff = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation()));
        var online = await Assert.ThrowsAsync<ForbiddenException>(
            () => _fixture.BookOnline.HandleAsync(new BookOnlineCommand(_fixture.Location, _fixture.Tomorrow8Pm, 2, "Awa", "+22997000001")));

        Assert.Equal(PlanFeatures.OverLimitCode, staff.Error.Code);
        Assert.Equal(PlanFeatures.OverLimitCode, online.Error.Code);
        Assert.Empty(_fixture.Reservations.Items);
        Assert.Equal(0, _fixture.LocksTaken);
    }

    [Fact]
    public async Task The_location_policy_drives_the_booking_window_and_party_size()
    {
        await _fixture.SetPolicy.HandleAsync(new SetReservationPolicyCommand(
            _fixture.Location, MinLeadTimeMinutes: 0, MaxAdvanceDays: 2, DefaultDurationMinutes: 90,
            CancellationCutoffMinutes: 60, MinDurationMinutes: 30, MaxDurationMinutes: 180, MaxPartySize: 6));

        var tooLarge = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 7)));
        var tooFar = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Create.HandleAsync(_fixture.NewReservation(startAt: _fixture.Tomorrow8Pm.AddDays(1))));
        var id = await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 6));
        var policy = await _fixture.GetPolicy.HandleAsync(new GetReservationPolicyQuery(_fixture.Location));

        Assert.Equal(ReservationErrors.PartySizeTooLarge, tooLarge.Error);
        Assert.Equal(ReservationErrors.BeyondBookingHorizon, tooFar.Error);
        Assert.Equal(TimeSpan.FromMinutes(90), _fixture.Reservations.Stored(id).Duration);
        Assert.Equal(new ReservationPolicyDto(0, 2, 90, 60, 30, 180, 6), policy);
    }

    [Fact]
    public async Task An_invalid_policy_is_refused_with_the_domain_error()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetPolicy.HandleAsync(new SetReservationPolicyCommand(
            _fixture.Location, 30, int.MaxValue, 120, 120, 15, 360, 50)));

        Assert.Equal(ReservationErrors.PolicyMaxAdvanceTooLong, refused.Error);
        Assert.Empty(_fixture.Policies.Items);
    }

    [Fact]
    public async Task The_whole_service_from_confirmation_to_completion()
    {
        var id = await _fixture.GivenReservationAsync(confirmed: true);
        var session = _fixture.GivenOpenSession(4, forReservation: true);

        await _fixture.Seat.HandleAsync(new SeatReservationCommand(id, session.Id.Value));
        await _fixture.Complete.HandleAsync(new CompleteReservationCommand(id));

        var read = await _fixture.GetById.HandleAsync(new GetReservationByIdQuery(id));
        Assert.Equal(ReservationStatus.Completed, read.Status);
        Assert.Equal(session.Id.Value, read.TableSessionId);
        Assert.Equal("+22990000000", read.GuestPhone);
    }

    [Fact]
    public async Task Seating_needs_an_open_session_of_the_same_location()
    {
        var id = await _fixture.GivenReservationAsync(confirmed: true);
        var elsewhere = _fixture.GivenOpenSession(4, locationId: _fixture.GivenLocation());

        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Seat.HandleAsync(new SeatReservationCommand(id, Guid.NewGuid())));
        var wrongLocation = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Seat.HandleAsync(new SeatReservationCommand(id, elsewhere.Id.Value)));
        await _fixture.Seat.HandleAsync(new SeatReservationCommand(id));

        Assert.Equal(ReservationsApplicationErrors.TableSessionNotFound, unknown.Error);
        Assert.Equal(ReservationsApplicationErrors.TableSessionNotUsable, wrongLocation.Error);
        Assert.Null(_fixture.Reservations.Stored(id).TableSessionId);
        Assert.Equal(ReservationStatus.Seated, _fixture.Reservations.Stored(id).Status);
    }

    [Fact]
    public async Task Moving_or_resizing_excludes_the_reservation_itself_and_is_checked_under_the_lock()
    {
        var id = await _fixture.GivenReservationAsync(partySize: 8);

        // 10 places : passer à 10 couverts ne compte pas les 8 actuels contre elle-même.
        await _fixture.ChangePartySize.HandleAsync(new ChangePartySizeCommand(id, 10));
        await _fixture.Reschedule.HandleAsync(new RescheduleReservationCommand(id, _fixture.Tomorrow8Pm.AddHours(1)));
        var other = await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 2, startAt: _fixture.Tomorrow8Pm.AddHours(-2)));
        var overlap = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Reschedule.HandleAsync(new RescheduleReservationCommand(other, _fixture.Tomorrow8Pm)));

        var moved = _fixture.Reservations.Stored(id);
        Assert.Equal((10, _fixture.Tomorrow8Pm.AddHours(1)), (moved.PartySize, moved.StartAtUtc));
        Assert.Equal(ReservationErrors.CapacityInsufficient, overlap.Error);
        Assert.Equal(4, _fixture.LocksTaken);
    }

    [Fact]
    public async Task The_same_slot_or_size_changes_nothing_but_still_refuses_a_finished_reservation()
    {
        var id = await _fixture.GivenReservationAsync();

        await _fixture.Reschedule.HandleAsync(new RescheduleReservationCommand(id, _fixture.Tomorrow8Pm));
        await _fixture.ChangePartySize.HandleAsync(new ChangePartySizeCommand(id, 4));
        await _fixture.Cancel.HandleAsync(new CancelReservationCommand(id, "Client malade"));
        var refused = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Reschedule.HandleAsync(new RescheduleReservationCommand(id, _fixture.Tomorrow8Pm)));

        Assert.Equal(ReservationErrors.AlreadyTerminal, refused.Error);
        Assert.Equal(0, _fixture.LocksTaken);
        var reservation = _fixture.Reservations.Stored(id);
        Assert.Equal(ReservationActor.Staff, reservation.CancelledBy);
        DomainAssert.NotRaised<ReservationRescheduled>(reservation);
    }

    [Fact]
    public async Task Staff_cancels_even_after_the_customer_cutoff_and_a_cancelled_reservation_frees_its_seats()
    {
        var id = await _fixture.GivenReservationAsync(partySize: 10);
        _fixture.Clock.Set(_fixture.Tomorrow8Pm.AddMinutes(-30));

        await _fixture.Cancel.HandleAsync(new CancelReservationCommand(id));

        Assert.Equal(ReservationStatus.Cancelled, _fixture.Reservations.Stored(id).Status);
        var availability = await _fixture.Availability.HandleAsync(new GetAvailabilityQuery(_fixture.Location, _fixture.Tomorrow8Pm, 10));
        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task A_no_show_is_marked_only_once_the_reservation_has_started()
    {
        var id = await _fixture.GivenReservationAsync(confirmed: true);

        var tooEarly = await Assert.ThrowsAsync<DomainException>(() => _fixture.NoShow.HandleAsync(new MarkReservationNoShowCommand(id)));
        _fixture.Clock.Set(_fixture.Tomorrow8Pm.AddMinutes(20));
        await _fixture.NoShow.HandleAsync(new MarkReservationNoShowCommand(id));

        Assert.Equal(ReservationErrors.NoShowBeforeStart, tooEarly.Error);
        Assert.Equal(ReservationStatus.NoShow, _fixture.Reservations.Stored(id).Status);
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_can_be_read_or_written()
    {
        var id = await _fixture.GivenReservationAsync();
        _fixture.CurrentTenantId = null;

        var read = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetById.HandleAsync(new GetReservationByIdQuery(id)));
        var write = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation()));

        Assert.Equal(TenancyErrors.TenantRequired, read.Error);
        Assert.Equal(TenancyErrors.TenantRequired, write.Error);
    }

    [Fact]
    public async Task A_reservation_of_another_tenant_is_not_found_and_a_suspended_restaurant_cannot_book()
    {
        var id = await _fixture.GivenReservationAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetReservationByIdQuery(id)));
        var cancel = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Cancel.HandleAsync(new CancelReservationCommand(id)));
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.WritableTenants.Remove(_fixture.TenantA);
        var suspended = await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation()));

        Assert.Equal(ReservationsApplicationErrors.ReservationNotFound, read.Error);
        Assert.Equal(ReservationsApplicationErrors.ReservationNotFound, cancel.Error);
        Assert.Equal(ReservationsApplicationErrors.TenantNotWritable, suspended.Error);
        Assert.Equal(ReservationStatus.Pending, _fixture.Reservations.Stored(id).Status);
        Assert.Equal(ReservationStatus.Pending, (await _fixture.GetById.HandleAsync(new GetReservationByIdQuery(id))).Status);
    }

    [Fact]
    public async Task The_agenda_lists_a_window_of_at_most_31_days_by_start()
    {
        var late = await _fixture.GivenReservationAsync(startAt: _fixture.Tomorrow8Pm.AddHours(1));
        var early = await _fixture.GivenReservationAsync(partySize: 2, startAt: _fixture.Tomorrow8Pm.AddHours(-6));
        var day = _fixture.Tomorrow8Pm.Date;
        var from = new DateTimeOffset(day, TimeSpan.Zero);

        var list = await _fixture.List.HandleAsync(new ListReservationsQuery(from, from.AddDays(1), _fixture.Location));
        var tooWide = await Assert.ThrowsAsync<DomainException>(() => _fixture.List.HandleAsync(new ListReservationsQuery(from, from.AddDays(32))));

        Assert.Equal(new[] { early, late }, list.Select(reservation => reservation.Id).ToArray());
        Assert.Equal(ReservationsApplicationErrors.WindowInvalid, tooWide.Error);
        Assert.Empty(await _fixture.List.HandleAsync(new ListReservationsQuery(from, from.AddDays(1), Guid.Empty)));
    }

    [Fact]
    public async Task A_reservation_can_be_taken_for_a_customer_but_an_erased_one_is_refused_before_the_capacity_lock()
    {
        var customer = _fixture.GivenCustomer();
        var erased = _fixture.GivenCustomer(isAnonymized: true);
        var elsewhere = _fixture.GivenCustomer(tenantId: _fixture.TenantB);

        var id = await _fixture.Create.HandleAsync(_fixture.NewReservation() with { CustomerId = customer });
        var locks = _fixture.LocksTaken;
        var anonymized = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Create.HandleAsync(_fixture.NewReservation() with { CustomerId = erased }));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Create.HandleAsync(_fixture.NewReservation() with { CustomerId = elsewhere }));

        var reservation = _fixture.Reservations.Stored(id);
        Assert.Equal(customer, reservation.CustomerId?.Value);
        // La réservation garde son propre instantané de contact.
        Assert.Equal("Awa Dossou", reservation.GuestContact.Name);
        Assert.Equal(locks, _fixture.LocksTaken);
        Assert.Equal((ReservationErrors.CustomerAnonymized, ReservationsApplicationErrors.CustomerNotFound), (anonymized.Error, unknown.Error));
    }

    [Fact]
    public async Task Anonymizing_a_customer_erases_the_contact_of_all_and_only_their_reservations_once()
    {
        var customer = _fixture.GivenCustomer();
        var first = await _fixture.Create.HandleAsync(_fixture.NewReservation() with { CustomerId = customer });
        var second = await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 2) with { CustomerId = customer });
        var someoneElse = await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 2));
        _fixture.WritableTenants.Remove(_fixture.TenantA);

        var erased = await _fixture.AnonymizeCustomer.HandleAsync(new AnonymizeCustomerReservationsCommand(customer));
        var replayed = await _fixture.AnonymizeCustomer.HandleAsync(new AnonymizeCustomerReservationsCommand(customer));

        Assert.Equal((2, 0), (erased, replayed));
        Assert.All(new[] { first, second }, id =>
        {
            var reservation = _fixture.Reservations.Stored(id);
            Assert.True(reservation.IsGuestAnonymized);
            Assert.Null(reservation.Notes);
            Assert.Equal(customer, reservation.CustomerId?.Value);
        });
        Assert.Equal("Awa Dossou", _fixture.Reservations.Stored(someoneElse).GuestContact.Name);
    }

    [Fact]
    public async Task A_reservation_can_be_attached_to_a_customer_detached_but_never_once_terminal()
    {
        var id = await _fixture.Create.HandleAsync(_fixture.NewReservation());
        var customer = _fixture.GivenCustomer();
        var erased = _fixture.GivenCustomer(isAnonymized: true);
        var elsewhere = _fixture.GivenCustomer(tenantId: _fixture.TenantB);

        await _fixture.ChangeCustomer.HandleAsync(new ChangeReservationCustomerCommand(id, customer));
        var attached = _fixture.Reservations.Stored(id).CustomerId?.Value;
        var anonymized = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ChangeCustomer.HandleAsync(new ChangeReservationCustomerCommand(id, erased)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.ChangeCustomer.HandleAsync(new ChangeReservationCustomerCommand(id, elsewhere)));
        await _fixture.ChangeCustomer.HandleAsync(new ChangeReservationCustomerCommand(id, null));
        await _fixture.Cancel.HandleAsync(new CancelReservationCommand(id));
        var terminal = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ChangeCustomer.HandleAsync(new ChangeReservationCustomerCommand(id, customer)));

        Assert.Equal(customer, attached);
        Assert.Null(_fixture.Reservations.Stored(id).CustomerId);
        Assert.Equal(ReservationErrors.CustomerAnonymized, anonymized.Error);
        Assert.Equal(ReservationsApplicationErrors.CustomerNotFound, unknown.Error);
        Assert.Equal(ReservationErrors.AlreadyTerminal, terminal.Error);
    }

    [Fact]
    public async Task A_customer_history_lists_only_their_reservations_page_by_page()
    {
        var customer = _fixture.GivenCustomer();
        var first = await _fixture.Create.HandleAsync(_fixture.NewReservation() with { CustomerId = customer });
        var second = await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 2) with { CustomerId = customer });
        await _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 2));

        var all = await _fixture.ListForCustomer.HandleAsync(new ListCustomerReservationsQuery(customer));
        var firstPage = await _fixture.ListForCustomer.HandleAsync(new ListCustomerReservationsQuery(customer, 1, 1));
        var empty = await _fixture.ListForCustomer.HandleAsync(new ListCustomerReservationsQuery(Guid.Empty));
        var invalid = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ListForCustomer.HandleAsync(new ListCustomerReservationsQuery(customer, PageSize: 0)));

        Assert.Equal(new[] { first, second }.Order(), all.Items.Select(reservation => reservation.Id).Order());
        Assert.Equal(2, all.TotalCount);
        Assert.True(firstPage.HasMore);
        Assert.Single(firstPage.Items);
        Assert.Empty(empty.Items);
        Assert.Equal(ReservationsApplicationErrors.PagingInvalid, invalid.Error);
    }
}

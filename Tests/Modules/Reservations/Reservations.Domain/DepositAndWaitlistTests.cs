namespace Reservations.Domain;

public sealed class DepositAndWaitlistTests
{
    private static readonly ReservationPolicy Policy = ReservationPolicy.Create(
        TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromHours(2), TimeSpan.FromHours(2));

    private static readonly DateTimeOffset Tomorrow8Pm = new(2026, 1, 16, 20, 0, 0, TimeSpan.Zero);

    private static readonly GuestContact Contact = GuestContact.Create("Awa Diallo", PhoneNumber.Create("+22997000000"), email: null);

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private static DomainError Error(Action action) => Assert.Throws<DomainException>(action).Error;

    private Reservation WithDeposit(decimal amount = 20000m)
    {
        var slot = Policy.SlotFor(Tomorrow8Pm);
        var decision = ReservationAvailabilityPolicy.Evaluate(_locationId, slot, 4, 20, [], null);
        var reservation = Reservation.Create(_tenantId, new LocationReference(_tenantId, _locationId, true), null, Contact, 4, slot, null, Policy, decision, _clock);
        reservation.RequireDeposit(Xof(amount), _clock.UtcNow.AddMinutes(30), _clock);
        reservation.ClearDomainEvents();
        return reservation;
    }

    [Fact]
    public void A_rule_asks_per_guest_from_a_party_size_or_always()
    {
        var fromSix = DepositRule.Create(DepositMode.FromPartySize, DepositBasis.PerGuest, Xof(5000m), 6, TimeSpan.FromMinutes(30));
        var always = DepositRule.Create(DepositMode.Always, DepositBasis.PerReservation, Xof(10000m), 99, TimeSpan.FromHours(2));

        Assert.Null(fromSix.AmountFor(5));
        Assert.Equal(Xof(30000m), fromSix.AmountFor(6));
        Assert.Equal(Xof(10000m), always.AmountFor(1));
        Assert.Null(DepositRule.Off.AmountFor(10));
        Assert.Equal(DepositErrors.AmountInvalid, Error(() => DepositRule.Create(DepositMode.Always, DepositBasis.PerGuest, Xof(0m), 1, TimeSpan.FromMinutes(30))));
        Assert.Equal(DepositErrors.PaymentWindowInvalid, Error(() => DepositRule.Create(DepositMode.Always, DepositBasis.PerGuest, Xof(1m), 1, TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void A_paid_deposit_confirms_the_pending_reservation()
    {
        var reservation = WithDeposit();

        reservation.RecordDepositSettlement(Xof(10000m), _clock);
        Assert.Equal(DepositStatus.AwaitingPayment, reservation.DepositStatus);
        reservation.RecordDepositSettlement(Xof(20000m), _clock);

        Assert.Equal((ReservationStatus.Confirmed, DepositStatus.Paid), (reservation.Status, reservation.DepositStatus));
        DomainAssert.Raised<ReservationDepositPaid>(reservation);
        DomainAssert.Raised<ReservationConfirmed>(reservation);
    }

    [Fact]
    public void An_unpaid_deposit_cancels_the_reservation_once_overdue_and_a_late_payment_is_refunded()
    {
        var reservation = WithDeposit();

        Assert.Equal(DepositErrors.NotDue, Error(() => reservation.ExpireUnpaidDeposit(Policy, _clock)));
        _clock.Advance(TimeSpan.FromMinutes(31));
        reservation.ExpireUnpaidDeposit(Policy, _clock);
        Assert.Equal((ReservationStatus.Cancelled, ReservationActor.System), (reservation.Status, reservation.CancelledBy));

        reservation.RecordDepositSettlement(Xof(20000m), _clock);
        Assert.Equal(DepositStatus.RefundRequested, reservation.DepositStatus);
        reservation.RecordDepositSettlement(Xof(0m), _clock);
        Assert.Equal(DepositStatus.Refunded, reservation.DepositStatus);
    }

    [Fact]
    public void Cancelling_refunds_a_paid_deposit_unless_the_staff_keeps_it_and_a_no_show_keeps_it()
    {
        var byGuest = WithDeposit();
        byGuest.RecordDepositSettlement(Xof(20000m), _clock);
        byGuest.Cancel(ReservationActor.Customer, null, Policy, _clock, keepDeposit: true);
        Assert.Equal(DepositStatus.RefundRequested, byGuest.DepositStatus);

        var late = WithDeposit();
        late.RecordDepositSettlement(Xof(20000m), _clock);
        late.Cancel(ReservationActor.Staff, "Annulation tardive", Policy, _clock, keepDeposit: true);
        Assert.Equal(DepositStatus.Forfeited, late.DepositStatus);

        var noShow = WithDeposit();
        noShow.RecordDepositSettlement(Xof(20000m), _clock);
        _clock.Advance(TimeSpan.FromDays(2));
        noShow.MarkNoShow(_clock);
        Assert.Equal(DepositStatus.Forfeited, noShow.DepositStatus);
        noShow.RequestDepositRefund(_clock);
        Assert.Equal(DepositStatus.RefundRequested, noShow.DepositStatus);
    }

    [Fact]
    public void A_deposit_is_applied_once_and_can_be_waived_while_awaited()
    {
        var paid = WithDeposit();
        paid.RecordDepositSettlement(Xof(20000m), _clock);
        paid.MarkDepositApplied(_clock);
        paid.MarkDepositApplied(_clock);
        paid.RecordDepositSettlement(Xof(0m), _clock);
        Assert.Equal(DepositStatus.Applied, paid.DepositStatus);

        var waived = WithDeposit();
        waived.WaiveDeposit(_clock);
        Assert.Equal(DepositStatus.Waived, waived.DepositStatus);
        Assert.Equal(DepositErrors.NotAwaitingPayment, Error(() => waived.ExpireUnpaidDeposit(Policy, _clock)));
        Assert.Equal(DepositErrors.AlreadyRequired, Error(() => waived.RequireDeposit(Xof(1m), _clock.UtcNow.AddHours(1), _clock)));
    }

    private WaitlistEntry Entry(int flexibility = 30) => WaitlistEntry.Create(
        _tenantId, new LocationReference(_tenantId, _locationId, true), null, Contact, 4, Tomorrow8Pm, flexibility, null, WaitlistSource.Online, Policy, _clock);

    [Fact]
    public void An_entry_is_offered_inside_its_window_and_released_back_to_waiting()
    {
        var entry = Entry();

        Assert.Equal(WaitlistErrors.OfferOutsideWindow, Error(() => entry.Offer(Tomorrow8Pm.AddMinutes(45), _clock.UtcNow.AddMinutes(15), _clock)));
        entry.Offer(Tomorrow8Pm.AddMinutes(30), _clock.UtcNow.AddMinutes(15), _clock);
        Assert.True(entry.HasLiveOffer(_clock.UtcNow));
        Assert.NotNull(entry.OfferHold(TimeSpan.FromHours(2), _clock.UtcNow));

        _clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(entry.OfferHold(TimeSpan.FromHours(2), _clock.UtcNow));
        entry.ReleaseOffer(_clock);
        Assert.Equal((WaitlistStatus.Waiting, 1), (entry.Status, entry.MissedOffers));
    }

    [Fact]
    public void An_entry_needs_a_phone_and_expires_when_its_time_has_passed()
    {
        var noPhone = GuestContact.Create("Kossi", null, Email.Create("k@example.com"));
        Assert.Equal(WaitlistErrors.PhoneRequired, Error(() => WaitlistEntry.Create(
            _tenantId, new LocationReference(_tenantId, _locationId, true), null, noPhone, 2, Tomorrow8Pm, 0, null, WaitlistSource.Staff, Policy, _clock)));

        var entry = Entry(0);
        entry.Offer(Tomorrow8Pm, Tomorrow8Pm, _clock);
        _clock.Advance(TimeSpan.FromDays(2));
        entry.ReleaseOffer(_clock);
        Assert.Equal(WaitlistStatus.Expired, entry.Status);
    }
}

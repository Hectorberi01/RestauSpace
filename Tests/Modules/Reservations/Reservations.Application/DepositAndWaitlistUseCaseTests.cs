using Reservations.Application.Commands;
using Reservations.Domain;

namespace Reservations.Application;

public sealed class DepositAndWaitlistUseCaseTests
{
    private readonly ReservationsFixture _fixture = new();

    private async Task GivenDepositRuleAsync(int fromPartySize = 6, decimal perGuest = 5000m) =>
        await _fixture.SetDepositRule.HandleAsync(new SetDepositRuleCommand(
            _fixture.Location, DepositMode.FromPartySize, DepositBasis.PerGuest, perGuest, fromPartySize, 30));

    [Fact]
    public async Task A_large_party_waits_for_its_deposit_and_is_confirmed_when_paid()
    {
        await GivenDepositRuleAsync();
        await _fixture.SetPolicy.HandleAsync(new SetReservationPolicyCommand(_fixture.Location, 30, 90, 120, 120, 15, 360, 50, AcceptsOnlineBookings: true));

        var small = await _fixture.BookOnline.HandleAsync(new BookOnlineCommand(_fixture.Location, _fixture.Tomorrow8Pm, 4, "Awa", "+22990000001"));
        var large = await _fixture.BookOnline.HandleAsync(new BookOnlineCommand(_fixture.Location, _fixture.Tomorrow8Pm, 6, "Kofi", "+22990000002"));

        Assert.Equal(ReservationStatus.Confirmed, _fixture.Reservations.Stored(small).Status);
        var reservation = _fixture.Reservations.Stored(large);
        Assert.Equal((ReservationStatus.Pending, DepositStatus.AwaitingPayment), (reservation.Status, reservation.DepositStatus));
        Assert.Equal(Money.Of(30000m, Currency.XOF), reservation.DepositAmount);
        Assert.Equal(_fixture.Clock.UtcNow.AddMinutes(30), reservation.DepositDueBeforeUtc);

        await _fixture.RecordDeposit.HandleAsync(new RecordDepositSettlementCommand(large, 30000m, "XOF"));
        Assert.Equal((ReservationStatus.Confirmed, DepositStatus.Paid), (reservation.Status, reservation.DepositStatus));
    }

    [Fact]
    public async Task The_staff_can_book_without_deposit_and_an_overdue_one_frees_the_table()
    {
        await GivenDepositRuleAsync(fromPartySize: 2);
        var without = await _fixture.Create.HandleAsync(_fixture.NewReservation() with { WithoutDeposit = true });
        var with = await _fixture.Create.HandleAsync(_fixture.NewReservation());

        Assert.False(_fixture.Reservations.Stored(without).HasDeposit);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        await _fixture.ExpireDeposit.HandleAsync(new ExpireUnpaidDepositCommand(with));
        await _fixture.ExpireDeposit.HandleAsync(new ExpireUnpaidDepositCommand(without));

        Assert.Equal(ReservationStatus.Cancelled, _fixture.Reservations.Stored(with).Status);
        Assert.Equal(ReservationStatus.Pending, _fixture.Reservations.Stored(without).Status);
    }

    [Fact]
    public async Task A_full_slot_is_offered_to_the_waitlist_when_a_table_frees_up_and_accepted()
    {
        _fixture.Seats[LocationId.From(_fixture.Location)] = 4;
        await _fixture.SetPolicy.HandleAsync(new SetReservationPolicyCommand(_fixture.Location, 30, 90, 120, 120, 15, 360, 50, AcceptsOnlineBookings: true));
        await _fixture.SetWaitlistSettings.HandleAsync(new SetWaitlistSettingsCommand(_fixture.Location, true, 15));
        var taken = await _fixture.GivenReservationAsync(partySize: 4, confirmed: true);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.JoinWaitlist.HandleAsync(new JoinWaitlistCommand(
            _fixture.Location, _fixture.Tomorrow8Pm, 4, 30, "Sena", "+22990000003", Online: true) with { PartySize = 1, DesiredStart = _fixture.Tomorrow8Pm.AddDays(1) }));
        Assert.Equal(ReservationsApplicationErrors.SlotAvailable, refused.Error);

        var entry = await _fixture.JoinWaitlist.HandleAsync(new JoinWaitlistCommand(_fixture.Location, _fixture.Tomorrow8Pm, 4, 30, "Sena", "+22990000003", Online: true));
        Assert.Equal(0, await _fixture.OfferFreed.HandleAsync(new OfferFreedCapacityCommand(_fixture.Location)));

        await _fixture.Cancel.HandleAsync(new CancelReservationCommand(taken));
        Assert.Equal(1, await _fixture.OfferFreed.HandleAsync(new OfferFreedCapacityCommand(_fixture.Location)));
        var offered = _fixture.Waitlist.Stored(entry);
        Assert.Equal((WaitlistStatus.Offered, _fixture.Tomorrow8Pm), (offered.Status, offered.OfferedStartUtc));

        // L'offre retient les places : personne d'autre ne les prend pendant ce temps.
        await Assert.ThrowsAsync<DomainException>(() => _fixture.Create.HandleAsync(_fixture.NewReservation(partySize: 4)));

        var reservation = await _fixture.AcceptOffer.HandleAsync(new AcceptWaitlistOfferCommand(entry));
        Assert.Equal(ReservationStatus.Confirmed, _fixture.Reservations.Stored(reservation).Status);
        Assert.Equal(WaitlistStatus.Booked, offered.Status);
    }

    [Fact]
    public async Task An_expired_offer_goes_back_to_waiting_and_a_passed_entry_expires()
    {
        _fixture.Seats[LocationId.From(_fixture.Location)] = 4;
        var entry = await _fixture.JoinWaitlist.HandleAsync(new JoinWaitlistCommand(_fixture.Location, _fixture.Tomorrow8Pm, 4, 0, "Sena", "+22990000003"));
        await _fixture.OfferFreed.HandleAsync(new OfferFreedCapacityCommand(_fixture.Location));
        var stored = _fixture.Waitlist.Stored(entry);
        Assert.Equal(WaitlistStatus.Offered, stored.Status);

        _fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await _fixture.ResolveWaitlist.HandleAsync(new ResolveWaitlistEntryCommand(entry));
        Assert.Equal((WaitlistStatus.Waiting, 1), (stored.Status, stored.MissedOffers));

        _fixture.Clock.Advance(TimeSpan.FromDays(2));
        await _fixture.ResolveWaitlist.HandleAsync(new ResolveWaitlistEntryCommand(entry));
        Assert.Equal(WaitlistStatus.Expired, stored.Status);
    }
}

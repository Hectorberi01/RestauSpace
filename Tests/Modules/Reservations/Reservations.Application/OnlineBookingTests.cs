using Reservations.Application.Commands;
using Reservations.Domain;

namespace Reservations.Application;

public sealed class OnlineBookingTests
{
    private readonly ReservationsFixture _fixture = new();

    private Task EnableAsync(bool online = true, int cutoffMinutes = 120) =>
        _fixture.SetPolicy.HandleAsync(new SetReservationPolicyCommand(
            _fixture.Location, MinLeadTimeMinutes: 30, MaxAdvanceDays: 30, DefaultDurationMinutes: 120, CancellationCutoffMinutes: cutoffMinutes,
            MinDurationMinutes: 15, MaxDurationMinutes: 360, MaxPartySize: 8, AcceptsOnlineBookings: online));

    private Task<Guid> BookAsync(int partySize = 4, DateTimeOffset? at = null) =>
        _fixture.BookOnline.HandleAsync(new BookOnlineCommand(_fixture.Location, at ?? _fixture.Tomorrow8Pm, partySize, "Awa", "+22997000001"));

    [Fact]
    public async Task A_location_takes_online_bookings_only_when_it_says_so()
    {
        var rules = await _fixture.OnlineRules.HandleAsync(new GetOnlineBookingRulesQuery(_fixture.Location));
        var refused = await Assert.ThrowsAsync<DomainException>(() => BookAsync());

        Assert.False(rules.AcceptsOnlineBookings);
        Assert.Equal(ReservationsApplicationErrors.OnlineBookingDisabled, refused.Error);
    }

    [Fact]
    public async Task An_online_booking_is_confirmed_at_once_and_holds_seats()
    {
        await EnableAsync();

        var id = await BookAsync(partySize: 8);
        var slots = await _fixture.CheckSlots.HandleAsync(new CheckOnlineSlotsQuery(
            _fixture.Location, 4, [_fixture.Tomorrow8Pm, _fixture.Tomorrow8Pm.AddHours(3), _fixture.Clock.UtcNow.AddMinutes(10)]));

        Assert.Equal(ReservationStatus.Confirmed, _fixture.Reservations.Stored(id).Status);
        Assert.Equal([false, false, true], slots.Select(slot => slot.IsAvailable));
    }

    [Fact]
    public async Task A_phone_number_is_required_online()
    {
        await EnableAsync();

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.BookOnline.HandleAsync(new BookOnlineCommand(_fixture.Location, _fixture.Tomorrow8Pm, 2, "Awa", null, "awa@example.com")));

        Assert.Equal(ReservationsApplicationErrors.ContactRequired, refused.Error);
    }

    [Fact]
    public async Task The_guest_cancels_until_the_cutoff_only()
    {
        await EnableAsync(cutoffMinutes: 120);
        var early = await BookAsync();
        var late = await BookAsync(partySize: 2, at: _fixture.Clock.UtcNow.AddMinutes(90));

        var view = await _fixture.GuestReservation.HandleAsync(new GetGuestReservationQuery(early));
        await _fixture.CancelAsGuest.HandleAsync(new CancelGuestReservationCommand(early));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.CancelAsGuest.HandleAsync(new CancelGuestReservationCommand(late)));

        Assert.True(view.CanCancel);
        Assert.Equal(_fixture.Tomorrow8Pm.AddHours(-2), view.CancelBeforeUtc);
        Assert.Equal((ReservationStatus.Cancelled, ReservationActor.Customer), (_fixture.Reservations.Stored(early).Status, _fixture.Reservations.Stored(early).CancelledBy));
        Assert.Equal(ReservationErrors.CancellationCutoffPassed, refused.Error);
    }

    [Fact]
    public async Task A_group_larger_than_allowed_finds_no_slot()
    {
        await EnableAsync();

        var slots = await _fixture.CheckSlots.HandleAsync(new CheckOnlineSlotsQuery(_fixture.Location, 9, [_fixture.Tomorrow8Pm]));

        Assert.False(slots.Single().IsAvailable);
    }
}

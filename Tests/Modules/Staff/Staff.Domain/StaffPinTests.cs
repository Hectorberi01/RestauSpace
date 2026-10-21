namespace Staff.Domain;

public sealed class StaffPinTests
{
    private readonly FakeClock _clock = new();

    private StaffMember ActiveMember()
    {
        var member = StaffMember.Invite(TenantId.New(), UserId.New(), true, _clock);
        member.Activate(_clock);
        return member;
    }

    [Fact]
    public void A_pin_has_four_to_six_digits_and_is_not_trivial()
    {
        Assert.Equal("2580", StaffPin.Validate(" 2580 "));
        Assert.Equal("190274", StaffPin.Validate("190274"));
        Assert.Equal(StaffMemberErrors.PinInvalidFormat, Assert.Throws<DomainException>(() => StaffPin.Validate("123")).Error);
        Assert.Equal(StaffMemberErrors.PinInvalidFormat, Assert.Throws<DomainException>(() => StaffPin.Validate("12a4")).Error);
        Assert.Equal(StaffMemberErrors.PinInvalidFormat, Assert.Throws<DomainException>(() => StaffPin.Validate("1234567")).Error);
        Assert.Equal(StaffMemberErrors.PinTooSimple, Assert.Throws<DomainException>(() => StaffPin.Validate("0000")).Error);
        Assert.Equal(StaffMemberErrors.PinTooSimple, Assert.Throws<DomainException>(() => StaffPin.Validate("1234")).Error);
        Assert.Equal(StaffMemberErrors.PinTooSimple, Assert.Throws<DomainException>(() => StaffPin.Validate("987654")).Error);
    }

    [Fact]
    public void Five_wrong_pins_block_the_pin_for_a_while_even_for_the_right_one()
    {
        var member = ActiveMember();
        member.SetPin("hash");

        for (var attempt = 0; attempt < StaffPin.MaxFailures - 1; attempt++)
        {
            Assert.False(member.TryPin(false, _clock));
        }

        Assert.False(member.TryPin(false, _clock));
        Assert.Equal(StaffMemberErrors.PinLocked, Assert.Throws<DomainException>(() => member.TryPin(true, _clock)).Error);

        _clock.Advance(StaffPin.Lockout);
        Assert.True(member.TryPin(true, _clock));
        Assert.Equal(0, member.PinFailures);
    }

    [Fact]
    public void A_right_pin_resets_the_count_and_a_member_without_pin_or_inactive_cannot_try()
    {
        var member = ActiveMember();
        Assert.Equal(StaffMemberErrors.PinNotSet, Assert.Throws<DomainException>(() => member.TryPin(true, _clock)).Error);

        member.SetPin("hash");
        member.TryPin(false, _clock);
        Assert.True(member.TryPin(true, _clock));
        Assert.Equal(0, member.PinFailures);

        member.Suspend(_clock);
        Assert.Equal(StaffMemberErrors.NotActive, Assert.Throws<DomainException>(() => member.TryPin(true, _clock)).Error);
    }

    [Fact]
    public void Leaving_forgets_the_pin()
    {
        var member = ActiveMember();
        member.SetPin("hash");

        member.Leave(_clock);

        Assert.False(member.HasPin);
    }

    [Fact]
    public void A_shift_left_open_too_long_is_closed_at_the_maximum_and_flagged()
    {
        var member = ActiveMember();
        var location = LocationId.New();
        member.AssignTenantWide(Role.Create(member.TenantId, "Serveur", isNameUnique: true, _clock).ToReference(), _clock);
        var entry = TimeEntry.ClockIn(member, location, _clock);

        _clock.Advance(TimeSpan.FromHours(20));
        entry.ClockOut(_clock);

        Assert.True(entry.ClosedAutomatically);
        Assert.Equal(TimeEntry.MaxShift, entry.ClockedOutAtUtc - entry.ClockedInAtUtc);
        Assert.Equal(TimeEntryErrors.AlreadyClosed, Assert.Throws<DomainException>(() => entry.ClockOut(_clock)).Error);
    }

    [Fact]
    public void A_till_needs_an_operational_location_a_name_and_a_sensible_lock()
    {
        var tenant = TenantId.New();
        var location = new LocationReference(tenant, LocationId.New(), true);

        var till = Till.Register(location, " Comptoir ", 5, "hash", _clock);

        Assert.Equal("Comptoir", till.Name);
        Assert.Equal(TillErrors.AutoLockInvalid, Assert.Throws<DomainException>(() => Till.Register(location, "Bar", 0, "hash", _clock)).Error);
        Assert.Equal(TillErrors.NameInvalid, Assert.Throws<DomainException>(() => Till.Register(location, " ", 5, "hash", _clock)).Error);
        Assert.Equal(
            StaffMemberErrors.LocationNotOperational,
            Assert.Throws<DomainException>(() => Till.Register(location with { IsOperational = false }, "Bar", 5, "hash", _clock)).Error);
        till.Revoke(_clock);
        Assert.False(till.IsActive);
        Assert.Equal(TillErrors.Revoked, Assert.Throws<DomainException>(() => till.Change("Bar", 5)).Error);
    }
}

namespace Reservations.Domain;

public sealed class ReservationPolicyTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);
    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);

    private readonly FakeClock _clock = new();

    private static ReservationPolicy Policy() => ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TwoHours);

    [Fact]
    public void Create_keeps_the_given_durations()
    {
        var policy = ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.FromMinutes(90), TimeSpan.FromHours(4));

        Assert.Equal(OneHour, policy.MinLeadTime);
        Assert.Equal(ThirtyDays, policy.MaxAdvance);
        Assert.Equal(TimeSpan.FromMinutes(90), policy.DefaultDuration);
        Assert.Equal(TimeSpan.FromHours(4), policy.CancellationCutoff);
    }

    [Fact]
    public void Create_accepts_no_lead_time_and_no_cancellation_cutoff()
    {
        var policy = ReservationPolicy.Create(TimeSpan.Zero, ThirtyDays, TwoHours, TimeSpan.Zero);

        Assert.True(policy.CanBookAt(_clock.UtcNow, _clock));
        Assert.True(policy.CanCancelAt(_clock.UtcNow, _clock));
    }

    [Fact]
    public void Create_rejects_a_negative_lead_time()
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyMinLeadTimeNegative,
            () => ReservationPolicy.Create(TimeSpan.FromMinutes(-1), ThirtyDays, TwoHours, TwoHours));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_horizon_that_is_not_positive(int days)
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxAdvanceNotPositive,
            () => ReservationPolicy.Create(TimeSpan.Zero, TimeSpan.FromDays(days), TwoHours, TwoHours));
    }

    [Fact]
    public void Create_rejects_a_horizon_that_does_not_exceed_the_lead_time()
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxAdvanceNotAfterLeadTime,
            () => ReservationPolicy.Create(TwoHours, TwoHours, TwoHours, TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxAdvanceNotAfterLeadTime,
            () => ReservationPolicy.Create(TwoHours, OneHour, TwoHours, TwoHours));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Create_rejects_a_default_duration_that_is_not_positive(int minutes)
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyDefaultDurationNotPositive,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.FromMinutes(minutes), TwoHours));
    }

    [Fact]
    public void Create_rejects_a_negative_cancellation_cutoff()
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyCancellationCutoffNegative,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TimeSpan.FromMinutes(-1)));
    }

    [Fact]
    public void CanBookAt_requires_the_minimum_lead_time()
    {
        var policy = Policy();
        var earliest = _clock.UtcNow + OneHour;

        Assert.False(policy.CanBookAt(_clock.UtcNow, _clock));
        Assert.False(policy.CanBookAt(earliest.AddSeconds(-1), _clock));
        Assert.True(policy.IsTooSoon(earliest.AddSeconds(-1), _clock));
        Assert.True(policy.CanBookAt(earliest, _clock));
        Assert.False(policy.IsTooSoon(earliest, _clock));
    }

    [Fact]
    public void CanBookAt_follows_the_clock()
    {
        var policy = Policy();
        var start = _clock.UtcNow + OneHour;
        Assert.True(policy.CanBookAt(start, _clock));

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(policy.CanBookAt(start, _clock));
    }

    [Fact]
    public void CanBookAt_stops_at_the_booking_horizon()
    {
        var policy = Policy();
        var latest = _clock.UtcNow + ThirtyDays;

        Assert.True(policy.CanBookAt(latest, _clock));
        Assert.False(policy.IsBeyondHorizon(latest, _clock));
        Assert.False(policy.CanBookAt(latest.AddSeconds(1), _clock));
        Assert.True(policy.IsBeyondHorizon(latest.AddSeconds(1), _clock));
    }

    [Fact]
    public void CanBookAt_compares_instants_not_wall_clock_times()
    {
        var policy = Policy();
        var earliestInParis = (_clock.UtcNow + OneHour).ToOffset(TimeSpan.FromHours(1));

        Assert.True(policy.CanBookAt(earliestInParis, _clock));
        Assert.False(policy.CanBookAt(earliestInParis.AddMinutes(-1), _clock));
    }

    [Fact]
    public void CanCancelAt_is_true_until_the_cutoff_included_and_false_after()
    {
        var policy = Policy();
        var start = _clock.UtcNow.AddHours(10);

        Assert.True(policy.CanCancelAt(start, _clock));

        _clock.Set(start - TwoHours);
        Assert.True(policy.CanCancelAt(start, _clock));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(policy.CanCancelAt(start, _clock));

        _clock.Set(start.AddHours(1));
        Assert.False(policy.CanCancelAt(start, _clock));
    }

    [Fact]
    public void SlotFor_uses_the_default_duration()
    {
        var start = new DateTimeOffset(2026, 2, 1, 19, 30, 0, TimeSpan.Zero);

        var slot = Policy().SlotFor(start);

        Assert.Equal(start, slot.StartUtc);
        Assert.Equal(start + TwoHours, slot.EndUtc);
    }

    [Fact]
    public void Default_policy_is_30_minutes_lead_90_days_horizon_2_hours_duration_2_hours_cutoff()
    {
        Assert.Equal(
            ReservationPolicy.Create(TimeSpan.FromMinutes(30), TimeSpan.FromDays(90), TwoHours, TwoHours),
            ReservationPolicy.Default);
    }

    [Fact]
    public void Create_defaults_the_duration_bounds_and_the_maximum_party_size()
    {
        var policy = Policy();

        Assert.Equal(TimeSpan.FromMinutes(15), policy.MinDuration);
        Assert.Equal(TimeSpan.FromHours(6), policy.MaxDuration);
        Assert.Equal(50, policy.MaxPartySize);
        Assert.Equal(policy.MinDuration, ReservationPolicy.Default.MinDuration);
        Assert.Equal(policy.MaxDuration, ReservationPolicy.Default.MaxDuration);
        Assert.Equal(policy.MaxPartySize, ReservationPolicy.Default.MaxPartySize);
    }

    [Fact]
    public void Create_keeps_explicit_duration_bounds_and_maximum_party_size()
    {
        var policy = ReservationPolicy.Create(
            OneHour, ThirtyDays, OneHour, TwoHours, TimeSpan.FromMinutes(30), TimeSpan.FromHours(3), maxPartySize: 12);

        Assert.Equal(TimeSpan.FromMinutes(30), policy.MinDuration);
        Assert.Equal(TimeSpan.FromHours(3), policy.MaxDuration);
        Assert.Equal(12, policy.MaxPartySize);
        Assert.True(policy.IsTooShort(TimeSpan.FromMinutes(29)));
        Assert.False(policy.IsTooShort(TimeSpan.FromMinutes(30)));
        Assert.False(policy.IsTooLong(TimeSpan.FromHours(3)));
        Assert.True(policy.IsTooLong(TimeSpan.FromHours(3).Add(TimeSpan.FromTicks(1))));
        Assert.NotEqual(Policy(), ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TwoHours, maxPartySize: 12));
        Assert.NotEqual(Policy(), ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TwoHours, maxDuration: TimeSpan.FromHours(3)));
    }

    [Fact]
    public void Create_rejects_invalid_duration_bounds()
    {
        static ReservationPolicy With(TimeSpan min, TimeSpan max) =>
            ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TwoHours, min, max);

        DomainAssert.Throws(ReservationErrors.PolicyDurationBoundsInvalid, () => With(TimeSpan.Zero, TwoHours));
        DomainAssert.Throws(ReservationErrors.PolicyDurationBoundsInvalid, () => With(TimeSpan.FromTicks(-1), TwoHours));
        DomainAssert.Throws(ReservationErrors.PolicyDurationBoundsInvalid, () => With(TimeSpan.FromHours(3), TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyDurationBoundsInvalid,
            () => With(OneHour, ReservationPolicy.MaxDurationCeiling + TimeSpan.FromTicks(1)));
        DomainAssert.Throws(ReservationErrors.PolicyDurationBoundsInvalid, () => With(OneHour, TimeSpan.MaxValue));
        Assert.Equal(ReservationPolicy.MaxDurationCeiling, With(OneHour, ReservationPolicy.MaxDurationCeiling).MaxDuration);
    }

    [Fact]
    public void Create_requires_the_default_duration_within_the_duration_bounds()
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyDefaultDurationOutOfBounds,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.FromMinutes(14), TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyDefaultDurationOutOfBounds,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.FromHours(7), TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyDefaultDurationOutOfBounds,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.MaxValue, TwoHours));
        Assert.Equal(
            TimeSpan.FromHours(7),
            ReservationPolicy.Create(OneHour, ThirtyDays, TimeSpan.FromHours(7), TwoHours, maxDuration: TimeSpan.FromHours(8)).DefaultDuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ReservationPolicy.MaxPartySizeCeiling + 1)]
    [InlineData(int.MaxValue)]
    public void Create_rejects_an_invalid_maximum_party_size(int maxPartySize)
    {
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxPartySizeInvalid,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TwoHours, maxPartySize: maxPartySize));
    }

    [Fact]
    public void Create_bounds_the_horizon_and_the_cutoff()
    {
        var ceiling = ReservationPolicy.MaxAdvanceCeiling;

        Assert.Equal(ceiling, ReservationPolicy.Create(OneHour, ceiling, TwoHours, ceiling).MaxAdvance);
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxAdvanceTooLong,
            () => ReservationPolicy.Create(OneHour, ceiling + TimeSpan.FromTicks(1), TwoHours, TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyMaxAdvanceTooLong,
            () => ReservationPolicy.Create(OneHour, TimeSpan.MaxValue, TwoHours, TwoHours));
        DomainAssert.Throws(
            ReservationErrors.PolicyCancellationCutoffTooLong,
            () => ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, TimeSpan.MaxValue));
    }

    [Fact]
    public void Time_arithmetic_never_throws_at_the_ends_of_the_calendar()
    {
        var policy = ReservationPolicy.Create(
            TimeSpan.Zero, ReservationPolicy.MaxAdvanceCeiling, TwoHours, ReservationPolicy.MaxAdvanceCeiling);
        var endOfTime = new FakeClock(DateTimeOffset.MaxValue);
        var startOfTime = new FakeClock(DateTimeOffset.MinValue);

        Assert.False(policy.CanBookAt(DateTimeOffset.MinValue, endOfTime));
        Assert.True(policy.IsTooSoon(DateTimeOffset.MinValue, endOfTime));
        Assert.True(policy.IsBeyondHorizon(DateTimeOffset.MaxValue, startOfTime));
        Assert.True(policy.CanBookAt(DateTimeOffset.MaxValue, endOfTime));
        Assert.False(policy.CanCancelAt(DateTimeOffset.MinValue, _clock));
        Assert.False(policy.CanCancelAt(DateTimeOffset.MinValue, endOfTime));
        Assert.True(policy.CanCancelAt(DateTimeOffset.MaxValue, startOfTime));
        DomainAssert.Throws(ReservationErrors.BeyondBookingHorizon, () => policy.SlotFor(DateTimeOffset.MaxValue));
    }

    [Fact]
    public void CanBookAt_reads_the_clock_once()
    {
        var clock = new CountingClock();

        Policy().CanBookAt(clock.Now.AddDays(1), clock);

        Assert.Equal(1, clock.Reads);
    }

    private sealed class CountingClock : IClock
    {
        public DateTimeOffset Now { get; } = FakeClock.DefaultNow;

        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                return Now;
            }
        }
    }

    [Fact]
    public void Policies_are_compared_by_value()
    {
        Assert.Equal(Policy(), Policy());
        Assert.NotEqual(Policy(), ReservationPolicy.Create(OneHour, ThirtyDays, TwoHours, OneHour));
    }
}

using Billing.Domain;

namespace Billing.Domain;

public class UsageCounterTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly FeatureCode Orders = FeatureCode.Create("orders.monthly");
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;
    private static readonly Period January = Period.Create(Now.AddDays(-14), Now.AddDays(17));
    private static readonly Period February = Period.Create(January.EndUtc, January.EndUtc.AddDays(28));

    private readonly FakeClock _clock = new();

    private static Entitlement Limit(long limit) => Entitlement.Limited(Orders, limit);

    private static UsageCounter NewCounter() => UsageCounter.Open(Tenant, Orders, January, isFirstCounterForFeature: true);

    private UsageCounter CounterAt(long used, long limit)
    {
        var counter = NewCounter();
        if (used > 0)
        {
            counter.Increment(used, Limit(limit), blocking: true, _clock);
        }

        counter.ClearDomainEvents();
        return counter;
    }

    [Fact]
    public void Open_starts_at_zero()
    {
        var counter = NewCounter();

        Assert.Equal(Tenant, counter.TenantId);
        Assert.Equal(Orders, counter.FeatureCode);
        Assert.Equal(January, counter.Period);
        Assert.Equal(0, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void Open_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => UsageCounter.Open(default, Orders, January, isFirstCounterForFeature: true));

    [Fact]
    public void Increment_reads_the_clock_once_so_a_boundary_increment_is_judged_and_stamped_at_one_instant()
    {
        var counter = NewCounter();
        // La première lecture tombe sur la dernière seconde de la période ; une seconde lecture serait hors période.
        var clock = new TickingClock(January.EndUtc.AddSeconds(-1));

        counter.Increment(10, Limit(10), blocking: true, clock);

        Assert.Equal(January.EndUtc.AddSeconds(-1), DomainAssert.Raised<QuotaReached>(counter).OccurredAtUtc);
    }

    [Fact]
    public void ResetForNewPeriod_reads_the_clock_once()
    {
        var counter = NewCounter();

        counter.ResetForNewPeriod(February, new TickingClock(January.EndUtc));

        Assert.Equal(January.EndUtc, DomainAssert.Raised<UsageCounterReset>(counter).OccurredAtUtc);
    }

    [Fact]
    public void Open_rejects_a_second_counter_for_the_same_feature() =>
        DomainAssert.Throws(
            UsageCounterErrors.AlreadyExists,
            () => UsageCounter.Open(Tenant, Orders, January, isFirstCounterForFeature: false));

    [Fact]
    public void Increment_adds_the_amount()
    {
        var counter = NewCounter();

        counter.Increment(3, Limit(10), blocking: true, _clock);
        counter.Increment(2, Limit(10), blocking: true, _clock);

        Assert.Equal(5, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Increment_rejects_a_non_positive_amount(long amount)
    {
        var counter = CounterAt(used: 4, limit: 10);

        DomainAssert.Throws(UsageCounterErrors.AmountNotPositive, () => counter.Increment(amount, Limit(10), blocking: false, _clock));

        Assert.Equal(4, counter.Used);
        Assert.True(counter.Used >= 0);
    }

    [Fact]
    public void Increment_rejects_an_entitlement_of_another_feature()
    {
        var counter = NewCounter();

        DomainAssert.Throws(
            UsageCounterErrors.FeatureMismatch,
            () => counter.Increment(1, Entitlement.Unlimited(FeatureCode.Create("kds")), blocking: true, _clock));

        Assert.Equal(0, counter.Used);
    }

    [Fact]
    public void Increment_rejects_usage_outside_the_period()
    {
        var counter = NewCounter();

        _clock.Set(January.StartUtc.AddTicks(-1));
        DomainAssert.Throws(UsageCounterErrors.OutsidePeriod, () => counter.Increment(1, Limit(10), blocking: false, _clock));
        _clock.Set(January.EndUtc);
        DomainAssert.Throws(UsageCounterErrors.OutsidePeriod, () => counter.Increment(1, Limit(10), blocking: true, _clock));

        Assert.Equal(0, counter.Used);

        _clock.Set(January.EndUtc.AddTicks(-1));
        counter.Increment(1, Limit(10), blocking: true, _clock);
        Assert.Equal(1, counter.Used);
    }

    [Fact]
    public void Blocking_increment_beyond_the_limit_is_rejected_and_changes_nothing()
    {
        var counter = CounterAt(used: 8, limit: 10);

        DomainAssert.Throws(UsageCounterErrors.QuotaExceeded, () => counter.Increment(3, Limit(10), blocking: true, _clock));

        Assert.Equal(8, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void Blocking_increment_up_to_the_limit_is_accepted_and_raises_QuotaReached()
    {
        var counter = CounterAt(used: 8, limit: 10);

        counter.Increment(2, Limit(10), blocking: true, _clock);

        Assert.Equal(10, counter.Used);
        var reached = DomainAssert.Raised<QuotaReached>(counter);
        Assert.Equal(
            (Tenant, counter.Id, Orders, January, 10L, 10L, Now),
            (reached.TenantId, reached.UsageCounterId, reached.FeatureCode, reached.Period, reached.Limit, reached.Used, reached.OccurredAtUtc));
    }

    [Fact]
    public void QuotaReached_is_not_raised_below_the_limit()
    {
        var counter = CounterAt(used: 0, limit: 10);

        counter.Increment(9, Limit(10), blocking: true, _clock);

        DomainAssert.NotRaised<QuotaReached>(counter);
    }

    [Fact]
    public void Two_increments_for_the_last_unit_only_the_first_one_wins()
    {
        var counter = CounterAt(used: 9, limit: 10);

        // Deux requêtes se disputent la dernière unité : la première à atteindre l'agrégat l'emporte.
        counter.Increment(1, Limit(10), blocking: true, _clock);
        DomainAssert.Throws(UsageCounterErrors.QuotaExceeded, () => counter.Increment(1, Limit(10), blocking: true, _clock));

        Assert.Equal(10, counter.Used);
        DomainAssert.Raised<QuotaReached>(counter);
    }

    [Fact]
    public void Blocking_increments_never_exceed_the_limit()
    {
        var counter = NewCounter();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                counter.Increment(3, Limit(10), blocking: true, _clock);
            }
            catch (DomainException)
            {
                // Attendu dès que le pas suivant dépasserait la limite.
            }
        }

        Assert.Equal(9, counter.Used);
        DomainAssert.NotRaised<QuotaReached>(counter);
    }

    [Fact]
    public void Blocking_increment_with_a_zero_limit_is_always_rejected()
    {
        var counter = NewCounter();

        DomainAssert.Throws(UsageCounterErrors.QuotaExceeded, () => counter.Increment(1, Limit(0), blocking: true, _clock));

        Assert.Equal(0, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void Blocking_increment_is_rejected_when_the_feature_is_disabled()
    {
        var counter = NewCounter();

        DomainAssert.Throws(UsageCounterErrors.FeatureDisabled, () => counter.Increment(1, Entitlement.Disabled(Orders), blocking: true, _clock));

        Assert.Equal(0, counter.Used);
    }

    [Fact]
    public void Unlimited_entitlement_never_blocks_nor_raises_QuotaReached()
    {
        var counter = NewCounter();

        counter.Increment(1_000_000, Entitlement.Unlimited(Orders), blocking: true, _clock);

        Assert.Equal(1_000_000, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void Non_blocking_increment_can_exceed_the_limit_and_raises_QuotaReached_once()
    {
        var counter = CounterAt(used: 8, limit: 10);

        counter.Increment(5, Limit(10), blocking: false, _clock);
        counter.Increment(5, Limit(10), blocking: false, _clock);

        Assert.Equal(18, counter.Used);
        Assert.Equal(13, DomainAssert.Raised<QuotaReached>(counter).Used);
    }

    [Fact]
    public void Non_blocking_increment_meters_a_disabled_feature_without_event()
    {
        var counter = NewCounter();

        counter.Increment(2, Entitlement.Disabled(Orders), blocking: false, _clock);

        Assert.Equal(2, counter.Used);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void QuotaReached_is_raised_again_after_the_limit_is_raised_and_reached()
    {
        var counter = CounterAt(used: 9, limit: 10);
        counter.Increment(1, Limit(10), blocking: true, _clock);
        counter.ClearDomainEvents();

        counter.Increment(10, Limit(20), blocking: true, _clock);

        Assert.Equal(20, DomainAssert.Raised<QuotaReached>(counter).Limit);
    }

    [Fact]
    public void ResetForNewPeriod_starts_the_next_period_at_zero()
    {
        var counter = CounterAt(used: 7, limit: 10);
        _clock.Set(February.StartUtc);

        counter.ResetForNewPeriod(February, _clock);

        Assert.Equal(0, counter.Used);
        Assert.Equal(February, counter.Period);
        var reset = DomainAssert.Raised<UsageCounterReset>(counter);
        Assert.Equal(
            (Tenant, counter.Id, Orders, January, 7L, February),
            (reset.TenantId, reset.UsageCounterId, reset.FeatureCode, reset.PreviousPeriod, reset.PreviousUsed, reset.Period));

        counter.Increment(10, Limit(10), blocking: true, _clock);
        Assert.Equal(10, counter.Used);
    }

    [Fact]
    public void ResetForNewPeriod_accepts_a_period_starting_after_a_gap()
    {
        var counter = CounterAt(used: 7, limit: 10);
        var later = Period.Create(January.EndUtc.AddDays(5), January.EndUtc.AddDays(35));
        _clock.Set(January.EndUtc);

        counter.ResetForNewPeriod(later, _clock);

        Assert.Equal(later, counter.Period);
        Assert.Equal(0, counter.Used);
    }

    [Fact]
    public void ResetForNewPeriod_rejects_a_period_starting_before_the_end_of_the_current_one()
    {
        var counter = CounterAt(used: 7, limit: 10);
        var overlapping = Period.Create(January.EndUtc.AddTicks(-1), January.EndUtc.AddDays(30));

        DomainAssert.Throws(UsageCounterErrors.PeriodNotAfterCurrent, () => counter.ResetForNewPeriod(overlapping, _clock));
        DomainAssert.Throws(UsageCounterErrors.PeriodNotAfterCurrent, () => counter.ResetForNewPeriod(January, _clock));

        Assert.Equal(7, counter.Used);
        Assert.Equal(January, counter.Period);
        DomainAssert.NoEvents(counter);
    }

    [Fact]
    public void ResetForNewPeriod_rejects_a_reset_while_the_current_period_is_still_running()
    {
        var counter = CounterAt(used: 7, limit: 10);

        DomainAssert.Throws(UsageCounterErrors.PeriodNotEnded, () => counter.ResetForNewPeriod(February, _clock));
        _clock.Set(January.EndUtc.AddTicks(-1));
        DomainAssert.Throws(UsageCounterErrors.PeriodNotEnded, () => counter.ResetForNewPeriod(February, _clock));

        Assert.Equal(7, counter.Used);
        Assert.Equal(January, counter.Period);
        DomainAssert.NoEvents(counter);

        _clock.Set(January.EndUtc);
        counter.ResetForNewPeriod(February, _clock);
        Assert.Equal(0, counter.Used);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Increment_that_would_overflow_is_rejected_with_a_domain_error(bool blocking)
    {
        var counter = NewCounter();
        counter.Increment(long.MaxValue - 1, Entitlement.Unlimited(Orders), blocking, _clock);

        DomainAssert.Throws(
            UsageCounterErrors.UsageOverflow,
            () => counter.Increment(2, Entitlement.Unlimited(Orders), blocking, _clock));

        Assert.Equal(long.MaxValue - 1, counter.Used);

        counter.Increment(1, Entitlement.Unlimited(Orders), blocking, _clock);
        Assert.Equal(long.MaxValue, counter.Used);
    }

    [Fact]
    public void Counter_enforces_the_quota_granted_by_a_plan()
    {
        var plan = Plan.Create("Starter", Money.Of(19m, Currency.EUR), BillingInterval.Monthly, [Limit(2)], _clock);
        var counter = NewCounter();
        var entitlement = plan.EntitlementFor(Orders);

        counter.Increment(1, entitlement, blocking: true, _clock);
        counter.Increment(1, entitlement, blocking: true, _clock);
        DomainAssert.Throws(UsageCounterErrors.QuotaExceeded, () => counter.Increment(1, entitlement, blocking: true, _clock));

        plan.SetEntitlement(Limit(3), _clock);
        counter.Increment(1, plan.EntitlementFor(Orders), blocking: true, _clock);
        Assert.Equal(3, counter.Used);
    }
}

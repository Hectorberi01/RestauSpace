namespace Loyalty.Domain;

public sealed class LoyaltyProgramTests
{
    private static Money Euros(decimal amount) => Money.Of(amount, Currency.EUR);

    [Fact]
    public void The_default_program_is_disabled_and_earns_nothing()
    {
        Assert.False(LoyaltyProgram.Default.IsEnabled);
        Assert.True(LoyaltyProgram.Default.PointsFor(Euros(100m)).IsZero);
        Assert.Null(LoyaltyProgram.Default.InactivityCutoff(FakeClock.DefaultNow));
    }

    [Theory]
    [InlineData(1, 23.99, 23)]
    [InlineData(2, 10.50, 21)]
    [InlineData(0.5, 9.99, 4)]
    [InlineData(0.01, 99.99, 0)]
    public void Points_are_the_amount_times_the_rate_rounded_down(decimal rate, decimal total, int expected)
    {
        var program = LoyaltyProgram.Create(isEnabled: true, rate, autoEnroll: true, inactivityExpiryDays: null);

        Assert.Equal(expected, program.PointsFor(Euros(total)).Value);
    }

    [Fact]
    public void A_zero_decimal_currency_and_a_fractional_rate_are_supported()
    {
        var program = LoyaltyProgram.Create(isEnabled: true, 0.0015m, autoEnroll: false, inactivityExpiryDays: null);

        Assert.Equal(22, program.PointsFor(Money.Of(15_000m, Currency.XOF)).Value);
    }

    [Fact]
    public void A_disabled_program_or_a_non_positive_total_earns_nothing()
    {
        var disabled = LoyaltyProgram.Create(isEnabled: false, 1m, autoEnroll: true, inactivityExpiryDays: null);
        var enabled = LoyaltyProgram.Create(isEnabled: true, 1m, autoEnroll: true, inactivityExpiryDays: null);

        Assert.True(disabled.PointsFor(Euros(50m)).IsZero);
        Assert.True(enabled.PointsFor(Euros(0m)).IsZero);
        Assert.True(enabled.PointsFor(Euros(-5m)).IsZero);
    }

    [Fact]
    public void Points_are_capped_at_the_largest_storable_value()
    {
        var program = LoyaltyProgram.Create(isEnabled: true, LoyaltyProgram.MaxPointsPerCurrencyUnit, autoEnroll: true, inactivityExpiryDays: null);

        Assert.Equal(int.MaxValue, program.PointsFor(Euros(Money.MaxAbsoluteAmount)).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000.0001)]
    [InlineData(0.00001)]
    public void The_rate_is_positive_bounded_and_has_at_most_four_decimals(decimal rate) =>
        DomainAssert.Throws(LoyaltyProgramErrors.RateOutOfRange, () => LoyaltyProgram.Create(true, rate, true, null));

    [Theory]
    [InlineData(29)]
    [InlineData(3651)]
    [InlineData(0)]
    public void The_inactivity_expiry_is_between_30_days_and_10_years(int days) =>
        DomainAssert.Throws(LoyaltyProgramErrors.ExpiryOutOfRange, () => LoyaltyProgram.Create(true, 1m, true, days));

    [Fact]
    public void The_inactivity_cutoff_is_now_minus_the_expiry_days()
    {
        var program = LoyaltyProgram.Create(isEnabled: true, 1m, autoEnroll: true, inactivityExpiryDays: 365);

        Assert.Equal(FakeClock.DefaultNow.AddDays(-365), program.InactivityCutoff(FakeClock.DefaultNow));
    }

    [Fact]
    public void Programs_are_compared_by_value()
    {
        Assert.Equal(LoyaltyProgram.Create(true, 1.5m, false, 90), LoyaltyProgram.Create(true, 1.5m, false, 90));
        Assert.NotEqual(LoyaltyProgram.Create(true, 1.5m, false, 90), LoyaltyProgram.Create(true, 1.5m, false, null));
    }
}

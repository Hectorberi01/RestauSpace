namespace SaasRestaurant.BuildingBlocks.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Of_rejects_more_decimals_than_the_currency_allows()
    {
        DomainAssert.Throws(CommonErrors.MoneyPrecision, () => Money.Of(10.001m, Currency.EUR));
        DomainAssert.Throws(CommonErrors.MoneyPrecision, () => Money.Of(100.5m, Currency.XOF));
    }

    [Fact]
    public void Rounded_rounds_half_away_from_zero_at_currency_precision()
    {
        Assert.Equal(Money.Of(10.01m, Currency.EUR), Money.Rounded(10.005m, Currency.EUR));
        Assert.Equal(Money.Of(-10.01m, Currency.EUR), Money.Rounded(-10.005m, Currency.EUR));
        Assert.Equal(Money.Of(101m, Currency.XOF), Money.Rounded(100.5m, Currency.XOF));
    }

    [Fact]
    public void Add_and_subtract_work_within_one_currency()
    {
        var total = Money.Of(10.50m, Currency.EUR) + Money.Of(2.25m, Currency.EUR) - Money.Of(0.75m, Currency.EUR);

        Assert.Equal(Money.Of(12.00m, Currency.EUR), total);
    }

    [Fact]
    public void Operations_between_currencies_are_rejected()
    {
        var euros = Money.Of(10m, Currency.EUR);
        var dollars = Money.Of(10m, Currency.USD);

        DomainAssert.Throws(CommonErrors.CurrencyMismatch, () => euros + dollars);
        DomainAssert.Throws(CommonErrors.CurrencyMismatch, () => euros - dollars);
        DomainAssert.Throws(CommonErrors.CurrencyMismatch, () => euros < dollars);
    }

    [Fact]
    public void Multiply_rounds_to_currency_precision()
    {
        Assert.Equal(Money.Of(3.33m, Currency.EUR), Money.Of(9.99m, Currency.EUR) * (1m / 3m));
    }

    [Fact]
    public void Equality_is_by_amount_and_currency()
    {
        Assert.Equal(Money.Of(5m, Currency.EUR), Money.Of(5.00m, Currency.EUR));
        Assert.NotEqual(Money.Of(5m, Currency.EUR), Money.Of(5m, Currency.USD));
        Assert.True(Money.Zero(Currency.EUR).IsZero);
        Assert.True(Money.Of(-1m, Currency.EUR).IsNegative);
    }
}

namespace SaasRestaurant.BuildingBlocks.Domain.Tests;

public class CurrencyTests
{
    [Theory]
    [InlineData("eur", "EUR", 2)]
    [InlineData(" XOF ", "XOF", 0)]
    [InlineData("TND", "TND", 3)]
    public void FromCode_normalizes_and_knows_minor_units(string input, string code, int decimals)
    {
        var currency = Currency.FromCode(input);

        Assert.Equal(code, currency.Code);
        Assert.Equal(decimals, currency.DecimalPlaces);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EURO")]
    [InlineData("ZZZ")]
    public void FromCode_rejects_unknown_codes(string? input)
    {
        DomainAssert.Throws(CommonErrors.CurrencyUnknown, () => Currency.FromCode(input));
    }
}

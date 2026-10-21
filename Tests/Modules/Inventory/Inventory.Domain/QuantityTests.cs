using System.Globalization;

namespace Inventory.Domain;

public class QuantityTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void Create_keeps_amount_and_unit()
    {
        var quantity = Quantity.Create(2.5m, UnitOfMeasure.Kilogram);

        Assert.Equal(2.5m, quantity.Amount);
        Assert.Equal(UnitOfMeasure.Kilogram, quantity.Unit);
        Assert.True(quantity.IsPositive);
        Assert.False(quantity.IsZero);
    }

    [Fact]
    public void Create_accepts_zero()
    {
        var quantity = Quantity.Create(0m, UnitOfMeasure.Piece);

        Assert.True(quantity.IsZero);
        Assert.False(quantity.IsPositive);
        Assert.Equal(quantity, Quantity.Zero(UnitOfMeasure.Piece));
    }

    [Fact]
    public void Create_rejects_a_negative_amount() =>
        DomainAssert.Throws(InventoryErrors.QuantityNegative, () => Quantity.Create(-0.001m, UnitOfMeasure.Gram));

    [Fact]
    public void Create_accepts_the_maximum_amount_and_rejects_above()
    {
        Assert.Equal(Quantity.MaxAmount, Quantity.Create(Quantity.MaxAmount, UnitOfMeasure.Gram).Amount);
        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => Quantity.Create(Quantity.MaxAmount + 0.001m, UnitOfMeasure.Gram));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(99)]
    public void Create_rejects_an_unknown_unit(int unit) =>
        DomainAssert.Throws(InventoryErrors.UnitUnknown, () => Quantity.Create(1m, (UnitOfMeasure)unit));

    [Theory]
    [InlineData(UnitOfMeasure.Gram, UnitDimension.Mass)]
    [InlineData(UnitOfMeasure.Kilogram, UnitDimension.Mass)]
    [InlineData(UnitOfMeasure.Milliliter, UnitDimension.Volume)]
    [InlineData(UnitOfMeasure.Liter, UnitDimension.Volume)]
    [InlineData(UnitOfMeasure.Piece, UnitDimension.Count)]
    public void Each_unit_belongs_to_one_dimension(UnitOfMeasure unit, UnitDimension expected) =>
        Assert.Equal(expected, unit.Dimension());

    [Fact]
    public void Dimension_rejects_an_unknown_unit() =>
        DomainAssert.Throws(InventoryErrors.UnitUnknown, () => ((UnitOfMeasure)42).Dimension());

    [Theory]
    [InlineData("1", UnitOfMeasure.Kilogram, UnitOfMeasure.Gram, "1000")]
    [InlineData("250", UnitOfMeasure.Gram, UnitOfMeasure.Kilogram, "0.25")]
    [InlineData("0.001", UnitOfMeasure.Kilogram, UnitOfMeasure.Gram, "1")]
    [InlineData("1", UnitOfMeasure.Gram, UnitOfMeasure.Kilogram, "0.001")]
    [InlineData("1.5", UnitOfMeasure.Liter, UnitOfMeasure.Milliliter, "1500")]
    [InlineData("330", UnitOfMeasure.Milliliter, UnitOfMeasure.Liter, "0.33")]
    [InlineData("3", UnitOfMeasure.Piece, UnitOfMeasure.Piece, "3")]
    [InlineData("0", UnitOfMeasure.Gram, UnitOfMeasure.Kilogram, "0")]
    public void ConvertTo_converts_exactly_inside_a_dimension(string amount, UnitOfMeasure from, UnitOfMeasure to, string expected)
    {
        var converted = Quantity.Create(D(amount), from).ConvertTo(to);

        Assert.Equal(D(expected), converted.Amount);
        Assert.Equal(to, converted.Unit);
    }

    [Fact]
    public void ConvertTo_round_trip_loses_nothing()
    {
        var original = Quantity.Create(0.333m, UnitOfMeasure.Kilogram);

        var roundTrip = original.ConvertTo(UnitOfMeasure.Gram).ConvertTo(UnitOfMeasure.Kilogram);

        Assert.Equal(original, roundTrip);
    }

    [Theory]
    [InlineData(UnitOfMeasure.Gram, UnitOfMeasure.Liter)]
    [InlineData(UnitOfMeasure.Kilogram, UnitOfMeasure.Milliliter)]
    [InlineData(UnitOfMeasure.Liter, UnitOfMeasure.Piece)]
    [InlineData(UnitOfMeasure.Piece, UnitOfMeasure.Kilogram)]
    [InlineData(UnitOfMeasure.Milliliter, UnitOfMeasure.Gram)]
    public void ConvertTo_rejects_another_dimension(UnitOfMeasure from, UnitOfMeasure to)
    {
        var quantity = Quantity.Create(1m, from);

        Assert.False(quantity.IsCompatibleWith(to));
        DomainAssert.Throws(InventoryErrors.UnitIncompatible, () => quantity.ConvertTo(to));
    }

    [Fact]
    public void ConvertTo_rejects_an_unknown_target_unit() =>
        DomainAssert.Throws(
            InventoryErrors.UnitUnknown,
            () => Quantity.Create(1m, UnitOfMeasure.Gram).ConvertTo((UnitOfMeasure)77));

    [Fact]
    public void Multiply_scales_the_amount_and_keeps_the_unit()
    {
        var total = Quantity.Create(0.15m, UnitOfMeasure.Kilogram).Multiply(4);

        Assert.Equal(Quantity.Create(0.6m, UnitOfMeasure.Kilogram), total);
    }

    [Fact]
    public void Multiply_rejects_a_negative_factor() =>
        DomainAssert.Throws(
            InventoryErrors.QuantityNegative,
            () => Quantity.Create(1m, UnitOfMeasure.Gram).Multiply(-1));

    [Fact]
    public void Quantities_are_equal_by_amount_and_unit()
    {
        Assert.Equal(Quantity.Create(1.0m, UnitOfMeasure.Gram), Quantity.Create(1.00m, UnitOfMeasure.Gram));
        Assert.NotEqual(Quantity.Create(1m, UnitOfMeasure.Gram), Quantity.Create(2m, UnitOfMeasure.Gram));
        Assert.NotEqual(Quantity.Create(1m, UnitOfMeasure.Gram), Quantity.Create(1m, UnitOfMeasure.Milliliter));
        // Égalité structurelle : des quantités équivalentes dans des unités différentes se comparent après ConvertTo.
        Assert.NotEqual(Quantity.Create(1m, UnitOfMeasure.Kilogram), Quantity.Create(1000m, UnitOfMeasure.Gram));
    }

    [Theory]
    [InlineData("0.000001")]
    [InlineData("12.123456")]
    [InlineData("999999999.999999")]
    public void Create_accepts_up_to_six_decimals(string amount) =>
        Assert.Equal(D(amount), Quantity.Create(D(amount), UnitOfMeasure.Kilogram).Amount);

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("1.1234567")]
    [InlineData("0.0000005")]
    public void Create_rejects_more_than_six_decimals(string amount)
    {
        DomainAssert.Throws(InventoryErrors.QuantityTooPrecise, () => Quantity.Create(D(amount), UnitOfMeasure.Kilogram));
        Assert.Equal("inventory.quantity.too_many_decimals", InventoryErrors.QuantityTooPrecise.Code);
        Assert.Equal(6, Quantity.MaxDecimals);
    }

    [Fact]
    public void Create_ignores_trailing_zeros_beyond_six_decimals() =>
        Assert.Equal(Quantity.Create(1.5m, UnitOfMeasure.Gram), Quantity.Create(D("1.50000000"), UnitOfMeasure.Gram));

    [Fact]
    public void ConvertTo_keeps_the_maximum_bound()
    {
        Assert.Equal(
            Quantity.MaxAmount,
            Quantity.Create(Quantity.MaxAmount / 1000m, UnitOfMeasure.Kilogram).ConvertTo(UnitOfMeasure.Gram).Amount);

        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => Quantity.Create(Quantity.MaxAmount / 1000m + 0.001m, UnitOfMeasure.Kilogram).ConvertTo(UnitOfMeasure.Gram));
        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => Quantity.Create(Quantity.MaxAmount, UnitOfMeasure.Liter).ConvertTo(UnitOfMeasure.Milliliter));
    }

    [Fact]
    public void ConvertTo_keeps_the_six_decimal_precision()
    {
        Assert.Equal(D("0.000001"), Quantity.Create(0.001m, UnitOfMeasure.Gram).ConvertTo(UnitOfMeasure.Kilogram).Amount);

        DomainAssert.Throws(
            InventoryErrors.QuantityTooPrecise,
            () => Quantity.Create(0.0001m, UnitOfMeasure.Gram).ConvertTo(UnitOfMeasure.Kilogram));
        DomainAssert.Throws(
            InventoryErrors.QuantityTooPrecise,
            () => Quantity.Create(D("0.000001"), UnitOfMeasure.Milliliter).ConvertTo(UnitOfMeasure.Liter));
    }

    [Fact]
    public void Multiply_keeps_the_maximum_bound()
    {
        Assert.Equal(Quantity.MaxAmount, Quantity.Create(Quantity.MaxAmount / 4m, UnitOfMeasure.Gram).Multiply(4).Amount);
        Assert.True(Quantity.Create(5m, UnitOfMeasure.Gram).Multiply(0).IsZero);

        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => Quantity.Create(Quantity.MaxAmount / 4m + 1m, UnitOfMeasure.Gram).Multiply(4));
        DomainAssert.Throws(
            InventoryErrors.QuantityTooLarge,
            () => Quantity.Create(Quantity.MaxAmount, UnitOfMeasure.Piece).Multiply(2));
    }
}

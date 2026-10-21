using Locations.Domain.Locations;

namespace Locations.Domain.Locations;

public sealed class LocationValueObjectsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LocationName_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(LocationErrors.NameEmpty, () => LocationName.Create(value));

    [Fact]
    public void LocationName_trims_and_enforces_its_length_limits()
    {
        Assert.Equal("Cotonou Centre", LocationName.Create("  Cotonou Centre ").Value);
        Assert.Equal(2, LocationName.Create("AB").Value.Length);
        Assert.Equal(100, LocationName.Create(new string('a', 100)).Value.Length);
        DomainAssert.Throws(LocationErrors.NameLengthInvalid, () => LocationName.Create("A"));
        DomainAssert.Throws(LocationErrors.NameLengthInvalid, () => LocationName.Create(new string('a', 101)));
    }

    [Fact]
    public void LocationName_is_compared_by_value() =>
        Assert.Equal(LocationName.Create("Cotonou Centre"), LocationName.Create(" Cotonou Centre"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(250)]
    public void Capacity_accepts_zero_and_positive_values(int seats) =>
        Assert.Equal(seats, Capacity.Create(seats).Seats);

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Capacity_rejects_a_negative_value(int seats) =>
        DomainAssert.Throws(LocationErrors.CapacityNegative, () => Capacity.Create(seats));

    [Fact]
    public void Capacity_is_compared_by_value()
    {
        Assert.Equal(Capacity.Create(40), Capacity.Create(40));
        Assert.Equal(Capacity.Zero, Capacity.Create(0));
        Assert.NotEqual(Capacity.Create(40), Capacity.Create(41));
    }

    [Fact]
    public void ExceptionalScheduleId_rejects_an_empty_guid_and_round_trips()
    {
        var id = ExceptionalScheduleId.New();

        DomainAssert.Throws(CommonErrors.EmptyId, () => ExceptionalScheduleId.From(Guid.Empty));
        Assert.Equal(id, ExceptionalScheduleId.From(id.Value));
    }
}

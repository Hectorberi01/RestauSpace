using Locations.Domain.Locations;

namespace Locations.Domain.Locations;

public sealed class AddressTests
{
    private static Address Create(
        string? street = "12 rue des Cheminots",
        string? city = "Cotonou",
        string? postalCode = null,
        string? country = "BJ",
        decimal? latitude = null,
        decimal? longitude = null) =>
        Address.Create(street, city, postalCode, country, latitude, longitude);

    [Fact]
    public void Create_trims_fields_and_normalizes_the_country_code()
    {
        var address = Address.Create(" 1 rue de Rivoli ", " Paris ", " 75001 ", " fr ");

        Assert.Equal("1 rue de Rivoli", address.Street);
        Assert.Equal("Paris", address.City);
        Assert.Equal("75001", address.PostalCode);
        Assert.Equal("FR", address.CountryCode.Value);
        Assert.False(address.HasCoordinates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_accepts_a_missing_postal_code(string? postalCode) =>
        Assert.Null(Create(postalCode: postalCode).PostalCode);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_an_empty_street(string? street) =>
        DomainAssert.Throws(LocationErrors.AddressStreetEmpty, () => Create(street: street));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_an_empty_city(string? city) =>
        DomainAssert.Throws(LocationErrors.AddressCityEmpty, () => Create(city: city));

    [Fact]
    public void Create_enforces_field_maximum_lengths()
    {
        Create(
            street: new string('a', Address.StreetMaxLength),
            city: new string('a', Address.CityMaxLength),
            postalCode: new string('1', Address.PostalCodeMaxLength));

        DomainAssert.Throws(LocationErrors.AddressFieldTooLong, () => Create(street: new string('a', Address.StreetMaxLength + 1)));
        DomainAssert.Throws(LocationErrors.AddressFieldTooLong, () => Create(city: new string('a', Address.CityMaxLength + 1)));
        DomainAssert.Throws(LocationErrors.AddressFieldTooLong, () => Create(postalCode: new string('1', Address.PostalCodeMaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ZZ")]
    [InlineData("XX")]
    [InlineData("UK")]
    [InlineData("EU")]
    [InlineData("FRA")]
    [InlineData("F")]
    [InlineData("12")]
    public void Create_rejects_an_unknown_country_code(string? country) =>
        DomainAssert.Throws(LocationErrors.CountryCodeUnknown, () => Create(country: country));

    [Theory]
    [InlineData("BJ")]
    [InlineData("FR")]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("CI")]
    [InlineData("SS")]
    [InlineData("gb")]
    public void CountryCode_accepts_assigned_iso_codes(string code) =>
        Assert.Equal(code.ToUpperInvariant(), CountryCode.Create(code).Value);

    [Fact]
    public void CountryCode_known_codes_cannot_be_extended_through_a_downcast()
    {
        Assert.False(CountryCode.KnownCodes is HashSet<string>);
        var asSet = Assert.IsAssignableFrom<ISet<string>>(CountryCode.KnownCodes);
        Assert.Throws<NotSupportedException>(() => asSet.Add("ZZ"));
        Assert.Throws<NotSupportedException>(() => asSet.Remove("BJ"));
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)asSet).Clear());

        Assert.Equal(249, CountryCode.KnownCodes.Count);
        DomainAssert.Throws(LocationErrors.CountryCodeUnknown, () => CountryCode.Create("ZZ"));
        Assert.Equal("BJ", CountryCode.Create("bj").Value);
    }

    [Fact]
    public void CountryCode_knows_the_249_assigned_iso_3166_1_codes()
    {
        Assert.Equal(249, CountryCode.KnownCodes.Count);
        Assert.All(CountryCode.KnownCodes, code => Assert.Matches("^[A-Z]{2}$", code));
    }

    [Fact]
    public void CountryCode_is_compared_by_value()
    {
        Assert.Equal(CountryCode.Create("bj"), CountryCode.Create("BJ"));
        Assert.NotEqual(CountryCode.Create("BJ"), CountryCode.Create("TG"));
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    [InlineData(6.3703, 2.3912)]
    public void Create_accepts_coordinates_up_to_their_limits(double latitude, double longitude)
    {
        var address = Create(latitude: (decimal)latitude, longitude: (decimal)longitude);

        Assert.True(address.HasCoordinates);
        Assert.Equal((decimal)latitude, address.Latitude);
        Assert.Equal((decimal)longitude, address.Longitude);
    }

    [Theory]
    [InlineData(90.0001)]
    [InlineData(-90.0001)]
    [InlineData(180)]
    public void Create_rejects_a_latitude_beyond_its_limits(double latitude) =>
        DomainAssert.Throws(
            LocationErrors.AddressLatitudeOutOfRange,
            () => Create(latitude: (decimal)latitude, longitude: 0m));

    [Theory]
    [InlineData(180.0001)]
    [InlineData(-180.0001)]
    [InlineData(360)]
    public void Create_rejects_a_longitude_beyond_its_limits(double longitude) =>
        DomainAssert.Throws(
            LocationErrors.AddressLongitudeOutOfRange,
            () => Create(latitude: 0m, longitude: (decimal)longitude));

    [Fact]
    public void Create_rejects_a_latitude_without_longitude_and_conversely()
    {
        DomainAssert.Throws(LocationErrors.AddressCoordinatesIncomplete, () => Create(latitude: 6.37m));
        DomainAssert.Throws(LocationErrors.AddressCoordinatesIncomplete, () => Create(longitude: 2.39m));
    }

    [Fact]
    public void Address_is_compared_by_value()
    {
        Assert.Equal(Create(latitude: 6.37m, longitude: 2.39m), Create(latitude: 6.37m, longitude: 2.39m));
        Assert.NotEqual(Create(latitude: 6.37m, longitude: 2.39m), Create());
        Assert.NotEqual(Create(city: "Porto-Novo"), Create());
        Assert.NotEqual(Create(country: "TG"), Create());
    }
}

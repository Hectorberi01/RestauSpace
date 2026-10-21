namespace Locations.Domain.Locations;

public sealed class Address : ValueObject
{
    public const int StreetMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int PostalCodeMaxLength = 20;
    public const decimal MaxLatitude = 90m;
    public const decimal MaxLongitude = 180m;

    private Address(string street, string city, string? postalCode, CountryCode countryCode, decimal? latitude, decimal? longitude)
    {
        Street = street;
        City = city;
        PostalCode = postalCode;
        CountryCode = countryCode;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Street { get; }
    public string City { get; }
    public string? PostalCode { get; }
    public CountryCode CountryCode { get; }
    public decimal? Latitude { get; }
    public decimal? Longitude { get; }
    public bool HasCoordinates => Latitude is not null;

    public static Address Create(
        string? street,
        string? city,
        string? postalCode,
        string? countryCode,
        decimal? latitude = null,
        decimal? longitude = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(street), LocationErrors.AddressStreetEmpty);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(city), LocationErrors.AddressCityEmpty);

        var trimmedStreet = street!.Trim();
        var trimmedCity = city!.Trim();
        var trimmedPostalCode = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
        DomainException.ThrowIf(
            trimmedStreet.Length > StreetMaxLength
            || trimmedCity.Length > CityMaxLength
            || trimmedPostalCode?.Length > PostalCodeMaxLength,
            LocationErrors.AddressFieldTooLong);

        var country = CountryCode.Create(countryCode);

        DomainException.ThrowIf(latitude is null != longitude is null, LocationErrors.AddressCoordinatesIncomplete);
        DomainException.ThrowIf(latitude is < -MaxLatitude or > MaxLatitude, LocationErrors.AddressLatitudeOutOfRange);
        DomainException.ThrowIf(longitude is < -MaxLongitude or > MaxLongitude, LocationErrors.AddressLongitudeOutOfRange);

        return new Address(trimmedStreet, trimmedCity, trimmedPostalCode, country, latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return PostalCode;
        yield return CountryCode;
        yield return Latitude;
        yield return Longitude;
    }

    public override string ToString() =>
        PostalCode is null ? $"{Street}, {City}, {CountryCode}" : $"{Street}, {PostalCode} {City}, {CountryCode}";
}

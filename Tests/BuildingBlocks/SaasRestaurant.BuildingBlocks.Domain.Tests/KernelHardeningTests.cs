namespace SaasRestaurant.BuildingBlocks.Domain.Tests;

public class KernelHardeningTests
{
    private sealed class Thing(Guid id) : Entity<Guid>(id);

    private sealed class OtherThing(Guid id) : Entity<Guid>(id);

    private sealed record Owned(TenantId TenantId) : ITenantOwned;

    [Fact]
    public void Guard_rejects_default_ids_and_returns_valid_ones()
    {
        var tenantId = TenantId.New();

        Assert.Equal(tenantId, Guard.NotEmpty(tenantId));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Guard.NotEmpty(default(TenantId)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Guard.NotEmpty(default(VariantId)));
    }

    [Fact]
    public void Two_empty_tenants_are_never_the_same_tenant()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => new Owned(default).EnsureSameTenant(default));
    }

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var id = Guid.NewGuid();

        Assert.True(new Thing(id) == new Thing(id));
        Assert.Equal(new Thing(id).GetHashCode(), new Thing(id).GetHashCode());
        Assert.True(new Thing(id) != new Thing(Guid.NewGuid()));
        Assert.False(new Thing(id).Equals(new OtherThing(id)));
        Assert.False(new Thing(id).Equals(null));
    }

    [Fact]
    public void Money_beyond_the_supported_range_is_a_business_error_not_an_overflow()
    {
        var max = Money.Of(Money.MaxAbsoluteAmount, Currency.XOF);

        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => Money.Of(Money.MaxAbsoluteAmount + 1m, Currency.XOF));
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => Money.Of(decimal.MinValue, Currency.XOF));
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => Money.Rounded(decimal.MaxValue, Currency.EUR));
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => max + Money.Of(1m, Currency.XOF));
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => max.Negate() - Money.Of(1m, Currency.XOF));
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => max * 2m);
        DomainAssert.Throws(CommonErrors.MoneyOutOfRange, () => max * decimal.MaxValue);
        Assert.Equal(max, max * 1m);
    }

    [Fact]
    public void Money_comparison_helpers_and_negative_factor()
    {
        var two = Money.Of(2m, Currency.EUR);
        var five = Money.Of(5m, Currency.EUR);

        Assert.Equal(two, Money.Min(two, five));
        Assert.Equal(five, Money.Max(two, five));
        Assert.True(two <= five && five >= two && two <= Money.Of(2m, Currency.EUR));
        Assert.Equal(Money.Of(-2m, Currency.EUR), two.Negate());
        Assert.Equal(Money.Of(-3.33m, Currency.EUR), Money.Of(9.99m, Currency.EUR) * (-1m / 3m));
        Assert.Equal(1, two.CompareTo(null));
        DomainAssert.Throws(CommonErrors.CurrencyMismatch, () => Money.Min(two, Money.Of(1m, Currency.USD)));
    }

    [Theory]
    [InlineData("SVC")]
    [InlineData("XCG")]
    [InlineData("VED")]
    public void Recently_added_iso_currencies_are_known(string code)
    {
        Assert.True(Currency.IsKnown(code));
        Assert.Equal(2, Currency.FromCode(code).DecimalPlaces);
    }

    [Theory]
    [InlineData("a..b@x.com")]
    [InlineData(".a@x.com")]
    [InlineData("a.@x.com")]
    [InlineData("a@1.2")]
    [InlineData("a@x.c")]
    public void Email_rejects_malformed_local_parts_and_numeric_tlds(string input)
    {
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => Email.Create(input));
    }

    [Fact]
    public void Email_bounds_local_part_and_total_length()
    {
        Assert.Equal(64, Email.Create(new string('a', 64) + "@x.com").Value.IndexOf('@'));
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => Email.Create(new string('a', 65) + "@x.com"));
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => Email.Create("a@" + new string('b', 250) + ".com"));
        Assert.Equal("first.last+tag@sub.example.co", Email.Create("First.Last+Tag@Sub.Example.co").Value);
    }

    [Theory]
    [InlineData("+1234567", true)]
    [InlineData("+123456789012345", true)]
    [InlineData("+123456", false)]
    [InlineData("+1234567890123456", false)]
    public void Phone_number_length_bounds(string input, bool valid)
    {
        if (valid)
        {
            Assert.Equal(input, PhoneNumber.Create(input).Value);
        }
        else
        {
            DomainAssert.Throws(CommonErrors.PhoneInvalid, () => PhoneNumber.Create(input));
        }
    }

    [Fact]
    public void Period_is_normalised_to_utc()
    {
        var start = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var period = Period.Create(start, start.AddHours(1));

        Assert.Equal(TimeSpan.Zero, period.StartUtc.Offset);
        Assert.Equal(8, period.StartUtc.Hour);
        Assert.Equal(TimeSpan.FromHours(1), period.Duration);
        Assert.Equal(Period.Create(start.ToUniversalTime(), start.AddHours(1).ToUniversalTime()), period);
    }

    [Theory]
    [InlineData("europe/paris")]
    [InlineData("EUROPE/PARIS")]
    [InlineData("utc")]
    [InlineData("posixrules")]
    [InlineData("Factory")]
    public void Time_zone_must_be_the_canonical_iana_spelling(string input)
    {
        DomainAssert.Throws(CommonErrors.TimeZoneUnknown, () => TimeZoneId.Create(input));
    }

    [Theory]
    [InlineData("Europe/Paris")]
    [InlineData("Africa/Porto-Novo")]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public void Canonical_time_zones_are_accepted(string input)
    {
        Assert.Equal(input, TimeZoneId.Create(input).Value);
    }
}

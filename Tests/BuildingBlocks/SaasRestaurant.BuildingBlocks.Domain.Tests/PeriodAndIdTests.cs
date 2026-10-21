namespace SaasRestaurant.BuildingBlocks.Domain.Tests;

public class PeriodAndIdTests
{
    private static readonly DateTimeOffset Start = FakeClock.DefaultNow;

    [Fact]
    public void Period_start_must_be_before_end()
    {
        DomainAssert.Throws(CommonErrors.PeriodInvalid, () => Period.Create(Start, Start));
        DomainAssert.Throws(CommonErrors.PeriodInvalid, () => Period.Create(Start, Start.AddSeconds(-1)));
    }

    [Fact]
    public void Period_is_half_open()
    {
        var period = Period.Create(Start, Start.AddHours(1));

        Assert.True(period.Contains(Start));
        Assert.False(period.Contains(Start.AddHours(1)));
        Assert.True(period.HasEndedAt(Start.AddHours(1)));
        Assert.False(period.Overlaps(Period.Create(Start.AddHours(1), Start.AddHours(2))));
        Assert.True(period.Overlaps(Period.Create(Start.AddMinutes(59), Start.AddHours(2))));
    }

    [Fact]
    public void Typed_ids_reject_empty_guid_and_compare_by_value()
    {
        var guid = Guid.NewGuid();

        DomainAssert.Throws(CommonErrors.EmptyId, () => TenantId.From(Guid.Empty));
        Assert.Equal(TenantId.From(guid), TenantId.From(guid));
        Assert.NotEqual(TenantId.New(), TenantId.New());
    }

    [Fact]
    public void Cross_tenant_reference_is_rejected()
    {
        var owner = new Owned(TenantId.New());

        owner.EnsureSameTenant(owner.TenantId);
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => owner.EnsureSameTenant(TenantId.New()));
    }

    private sealed record Owned(TenantId TenantId) : ITenantOwned;
}

public class TimeZoneIdTests
{
    [Fact]
    public void Known_iana_zone_converts_an_instant_to_local_wall_clock()
    {
        var paris = TimeZoneId.Create(" Europe/Paris ");
        var summer = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var winter = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("Europe/Paris", paris.Value);
        Assert.Equal(12, paris.ToLocal(summer).Hour);
        Assert.Equal(11, paris.ToLocal(winter).Hour);
        Assert.Equal(11, TimeZoneId.Create("Africa/Porto-Novo").ToLocal(summer).Hour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Time_zone_is_required(string? input)
    {
        DomainAssert.Throws(CommonErrors.TimeZoneEmpty, () => TimeZoneId.Create(input));
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("Romance Standard Time")]
    public void Unknown_or_windows_zone_is_rejected(string input)
    {
        DomainAssert.Throws(CommonErrors.TimeZoneUnknown, () => TimeZoneId.Create(input));
    }
}

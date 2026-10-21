using Tenants.Domain.Tenants;

namespace Tenants.Domain.Tenants;

public sealed class TenantValueObjectsTests
{
    // ---- TenantName ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TenantName_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(TenantErrors.NameEmpty, () => TenantName.Create(value));

    [Fact]
    public void TenantName_trims_the_value() =>
        Assert.Equal("Chez Hector", TenantName.Create("  Chez Hector ").Value);

    [Fact]
    public void TenantName_enforces_its_length_limits()
    {
        Assert.Equal(2, TenantName.Create("AB").Value.Length);
        Assert.Equal(100, TenantName.Create(new string('a', 100)).Value.Length);
        DomainAssert.Throws(TenantErrors.NameLengthInvalid, () => TenantName.Create("A"));
        DomainAssert.Throws(TenantErrors.NameLengthInvalid, () => TenantName.Create(new string('a', 101)));
    }

    [Theory]
    [InlineData("Che\0z")]
    [InlineData("Chez\tHector")]
    [InlineData("Chez\nHector")]
    public void TenantName_rejects_control_characters(string value) =>
        DomainAssert.Throws(TenantErrors.NameInvalidCharacters, () => TenantName.Create(value));

    [Fact]
    public void TenantName_is_compared_by_value()
    {
        Assert.Equal(TenantName.Create("Chez Hector"), TenantName.Create(" Chez Hector"));
        Assert.NotEqual(TenantName.Create("Chez Hector"), TenantName.Create("Chez Paul"));
    }

    // ---- TimeZoneId ----

    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/Paris")]
    [InlineData("Africa/Porto-Novo")]
    [InlineData("America/Argentina/Buenos_Aires")]
    public void TimeZoneId_accepts_iana_identifiers(string id) =>
        Assert.Equal(id, TimeZoneId.Create($" {id} ").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TimeZoneId_rejects_an_empty_value(string? id) =>
        DomainAssert.Throws(CommonErrors.TimeZoneEmpty, () => TimeZoneId.Create(id));

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Europe/Atlantis")]
    [InlineData("Romance Standard Time")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("+01:00")]
    [InlineData("Europe/")]
    [InlineData("../etc/passwd")]
    public void TimeZoneId_rejects_unknown_or_non_iana_identifiers(string id) =>
        DomainAssert.Throws(CommonErrors.TimeZoneUnknown, () => TimeZoneId.Create(id));

    [Fact]
    public void TimeZoneId_rejects_a_value_longer_than_the_maximum() =>
        DomainAssert.Throws(CommonErrors.TimeZoneUnknown, () => TimeZoneId.Create("Europe/" + new string('a', 64)));

    [Fact]
    public void TimeZoneId_is_compared_by_value_and_exposes_utc()
    {
        Assert.Equal(TimeZoneId.Create("Europe/Paris"), TimeZoneId.Create("Europe/Paris"));
        Assert.NotEqual(TimeZoneId.Create("Europe/Paris"), TimeZoneId.Create("Africa/Porto-Novo"));
        Assert.Equal(TimeZoneId.Create("UTC"), TimeZoneId.Utc);
    }

    // ---- Branding ----

    [Fact]
    public void Branding_can_be_created_with_a_display_name_only()
    {
        var branding = Branding.Create("  Chez Hector ");

        Assert.Equal("Chez Hector", branding.DisplayName);
        Assert.Null(branding.LogoUrl);
        Assert.Null(branding.PrimaryColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Branding_rejects_an_empty_display_name(string? name) =>
        DomainAssert.Throws(TenantErrors.BrandingDisplayNameEmpty, () => Branding.Create(name));

    [Theory]
    [InlineData("Che\0z")]
    [InlineData("Chez\nHector")]
    public void Branding_rejects_control_characters_in_the_display_name(string name) =>
        DomainAssert.Throws(TenantErrors.BrandingDisplayNameInvalidCharacters, () => Branding.Create(name));

    [Fact]
    public void Branding_enforces_the_display_name_maximum_length()
    {
        Assert.Equal(100, Branding.Create(new string('a', 100)).DisplayName.Length);
        DomainAssert.Throws(TenantErrors.BrandingDisplayNameTooLong, () => Branding.Create(new string('a', 101)));
    }

    [Theory]
    [InlineData("#1a2b3c", "#1A2B3C")]
    [InlineData("#1A2B3C", "#1A2B3C")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData(" #FFF ", "#FFFFFF")]
    public void Branding_accepts_and_normalizes_a_valid_hex_color(string input, string expected) =>
        Assert.Equal(expected, Branding.Create("Chez Hector", primaryColor: input).PrimaryColor);

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("1A2B3C")]
    [InlineData("#1A2B3")]
    [InlineData("#1A2B3C4")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#1A2B3CFF")]
    [InlineData("rgb(1,2,3)")]
    public void Branding_rejects_an_invalid_color(string color) =>
        DomainAssert.Throws(TenantErrors.BrandingColorInvalid, () => Branding.Create("Chez Hector", primaryColor: color));

    [Theory]
    [InlineData("https://cdn.example.com/logo.png")]
    [InlineData("http://cdn.example.com/logo.png")]
    public void Branding_accepts_an_absolute_http_logo_url(string url) =>
        Assert.Equal(new Uri(url), Branding.Create("Chez Hector", new Uri(url)).LogoUrl);

    [Fact]
    public void Branding_rejects_a_relative_logo_url() =>
        DomainAssert.Throws(
            TenantErrors.BrandingLogoUrlInvalid,
            () => Branding.Create("Chez Hector", new Uri("/logo.png", UriKind.Relative)));

    [Theory]
    [InlineData("ftp://cdn.example.com/logo.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void Branding_rejects_a_logo_url_that_is_not_http(string url) =>
        DomainAssert.Throws(TenantErrors.BrandingLogoUrlInvalid, () => Branding.Create("Chez Hector", new Uri(url)));

    [Fact]
    public void Branding_is_compared_by_value()
    {
        var logo = new Uri("https://cdn.example.com/logo.png");

        Assert.Equal(Branding.Create("Chez Hector", logo, "#abc"), Branding.Create("Chez Hector", logo, "#AABBCC"));
        Assert.NotEqual(Branding.Create("Chez Hector", logo, "#abc"), Branding.Create("Chez Hector", logo, "#abd"));
        Assert.NotEqual(Branding.Create("Chez Hector", logo), Branding.Create("Chez Hector"));
    }

    [Fact]
    public void Branding_distinguishes_logo_urls_differing_only_by_their_fragment()
    {
        var first = Branding.Create("Chez Hector", new Uri("https://cdn.example.com/logos.svg#light"));
        var second = Branding.Create("Chez Hector", new Uri("https://cdn.example.com/logos.svg#dark"));

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first, Branding.Create("Chez Hector", new Uri("https://cdn.example.com/logos.svg#light")));
    }
}

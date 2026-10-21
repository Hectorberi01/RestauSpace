using Tenants.Application.Commands;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

public sealed class TenantSettingsTests
{
    private readonly TenantsFixture _fixture = new();

    private ChangeTenantDefaultCurrencyHandler CurrencyHandler => new(_fixture, _fixture, _fixture, _fixture.Clock);

    private UpdateTenantBrandingHandler BrandingHandler => new(_fixture, _fixture, _fixture.Clock);

    [Fact]
    public async Task Rename_changes_the_name()
    {
        var tenant = _fixture.GivenTenant();

        await new RenameTenantHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new RenameTenantCommand(tenant.Id.Value, "Maquis du Port"));

        Assert.Equal("Maquis du Port", tenant.Name.Value);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task ChangeTimeZone_changes_the_time_zone()
    {
        var tenant = _fixture.GivenTenant();

        await new ChangeTenantTimeZoneHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, "Europe/Paris"));

        Assert.Equal("Europe/Paris", tenant.TimeZone.Value);
    }

    [Fact]
    public async Task ChangePriceTaxMode_switches_the_mode()
    {
        var tenant = _fixture.GivenTenant();

        await new ChangeTenantPriceTaxModeHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new ChangeTenantPriceTaxModeCommand(tenant.Id.Value, PricesIncludeTax: false));

        Assert.False(tenant.PricesIncludeTax);
    }

    [Fact]
    public async Task ChangeDefaultCurrency_changes_the_currency_when_the_catalog_is_not_priced()
    {
        var tenant = _fixture.GivenTenant();

        await CurrencyHandler.HandleAsync(new ChangeTenantDefaultCurrencyCommand(tenant.Id.Value, "eur"));

        Assert.Equal(Currency.EUR, tenant.DefaultCurrency);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task ChangeDefaultCurrency_is_rejected_once_the_catalog_is_priced()
    {
        var tenant = _fixture.GivenTenant();
        _fixture.HasPricedCatalog = true;

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => CurrencyHandler.HandleAsync(new ChangeTenantDefaultCurrencyCommand(tenant.Id.Value, "EUR")));

        Assert.Equal(TenantErrors.CurrencyLockedByPricedCatalog.Code, exception.Code);
        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task ChangeDefaultCurrency_to_the_same_currency_does_not_query_the_catalog()
    {
        var tenant = _fixture.GivenTenant();
        _fixture.HasPricedCatalog = true;

        await CurrencyHandler.HandleAsync(new ChangeTenantDefaultCurrencyCommand(tenant.Id.Value, "XOF"));

        Assert.Equal(0, _fixture.PricedCatalogChecks);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public async Task UpdateBranding_replaces_the_branding()
    {
        var tenant = _fixture.GivenTenant();

        await BrandingHandler.HandleAsync(new UpdateTenantBrandingCommand(
            tenant.Id.Value, "Hector's", "https://cdn.example.com/logo.png", "#abc"));

        Assert.Equal("Hector's", tenant.Branding.DisplayName);
        Assert.Equal("https://cdn.example.com/logo.png", tenant.Branding.LogoUrl!.AbsoluteUri);
        Assert.Equal("#AABBCC", tenant.Branding.PrimaryColor);
    }

    [Fact]
    public async Task UpdateBranding_without_logo_or_color_removes_them()
    {
        var tenant = _fixture.GivenTenant();
        await BrandingHandler.HandleAsync(new UpdateTenantBrandingCommand(
            tenant.Id.Value, "Hector's", "https://cdn.example.com/logo.png", "#abc"));

        await BrandingHandler.HandleAsync(new UpdateTenantBrandingCommand(tenant.Id.Value, "Hector's", LogoUrl: " "));

        Assert.Null(tenant.Branding.LogoUrl);
        Assert.Null(tenant.Branding.PrimaryColor);
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("/images/logo.png")]
    [InlineData("ftp://cdn.example.com/logo.png")]
    public async Task UpdateBranding_rejects_a_logo_url_that_is_not_absolute_http(string logoUrl)
    {
        var tenant = _fixture.GivenTenant();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => BrandingHandler.HandleAsync(new UpdateTenantBrandingCommand(tenant.Id.Value, "Hector's", logoUrl)));

        Assert.Equal(TenantErrors.BrandingLogoUrlInvalid.Code, exception.Code);
        Assert.Equal(0, _fixture.SaveCount);
    }
}

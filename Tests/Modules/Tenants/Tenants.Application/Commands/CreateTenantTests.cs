using Tenants.Application.Commands;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

public sealed class CreateTenantTests
{
    private readonly TenantsFixture _fixture = new();

    private CreateTenantHandler Handler => new(_fixture, _fixture, _fixture.Clock);

    [Fact]
    public async Task Creates_and_saves_a_trial_tenant_and_returns_its_id()
    {
        var id = await Handler.HandleAsync(new CreateTenantCommand(" Chez Hector ", "xof", "Africa/Porto-Novo"));

        var tenant = _fixture.Single;
        Assert.Equal(tenant.Id.Value, id);
        Assert.Equal("Chez Hector", tenant.Name.Value);
        Assert.Equal(TenantStatus.Trial, tenant.Status);
        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        Assert.Equal("Africa/Porto-Novo", tenant.TimeZone.Value);
        Assert.True(tenant.PricesIncludeTax);
        Assert.Equal(1, _fixture.SaveCount);
        DomainAssert.Raised<TenantCreated>(tenant);
        DomainAssert.NotRaised<TenantPriceTaxModeChanged>(tenant);
    }

    [Fact]
    public async Task Can_create_a_tax_exclusive_tenant()
    {
        await Handler.HandleAsync(new CreateTenantCommand("Chez Hector", "EUR", "Europe/Paris", PricesIncludeTax: false));

        Assert.False(_fixture.Single.PricesIncludeTax);
    }

    [Theory]
    [InlineData("", "XOF", "Africa/Porto-Novo", "tenants.tenant_name.empty")]
    [InlineData("Chez Hector", "???", "Africa/Porto-Novo", "common.currency.unknown")]
    [InlineData("Chez Hector", "XOF", "Romance Standard Time", "common.time_zone.unknown")]
    public async Task Invalid_input_is_rejected_and_nothing_is_saved(
        string name, string currency, string timeZone, string expectedCode)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Handler.HandleAsync(new CreateTenantCommand(name, currency, timeZone)));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(0, _fixture.SaveCount);
    }
}

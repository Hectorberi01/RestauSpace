using Tenants.Application.Queries;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Queries;

public sealed class GetTenantByIdTests
{
    private readonly TenantsFixture _fixture = new();

    private GetTenantByIdHandler Handler => new(_fixture);

    [Fact]
    public async Task Returns_the_tenant_and_its_settings()
    {
        var tenant = _fixture.GivenTenant();

        var dto = await Handler.HandleAsync(new GetTenantByIdQuery(tenant.Id.Value));

        Assert.Equal(tenant.Id.Value, dto.Id);
        Assert.Equal("Chez Hector", dto.Name);
        Assert.Equal(TenantStatus.Trial, dto.Status);
        Assert.True(dto.CanAcceptBusinessWrites);
        Assert.Equal("XOF", dto.DefaultCurrencyCode);
        Assert.Equal("Africa/Porto-Novo", dto.TimeZoneId);
        Assert.True(dto.PricesIncludeTax);
        Assert.Equal(new BrandingDto("Chez Hector", null, null), dto.Branding);
        Assert.Equal(_fixture.Clock.UtcNow, dto.CreatedAtUtc);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_suspended_tenant_stays_readable_and_reports_its_suspension()
    {
        var tenant = _fixture.GivenTenant();
        tenant.Suspend(TenantSuspensionReason.PaymentOverdue, _fixture.Clock);

        var dto = await Handler.HandleAsync(new GetTenantByIdQuery(tenant.Id.Value));

        Assert.Equal(TenantStatus.Suspended, dto.Status);
        Assert.False(dto.CanAcceptBusinessWrites);
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, dto.SuspensionReason);
        Assert.Equal(_fixture.Clock.UtcNow, dto.SuspendedAtUtc);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => Handler.HandleAsync(new GetTenantByIdQuery(Guid.NewGuid())));

        Assert.Equal(TenantApplicationErrors.NotFound.Code, exception.Code);
    }
}

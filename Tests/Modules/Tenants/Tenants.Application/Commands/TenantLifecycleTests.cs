using Tenants.Application.Commands;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Commands;

public sealed class TenantLifecycleTests
{
    private readonly TenantsFixture _fixture = new();

    [Fact]
    public async Task Activate_moves_the_tenant_to_active_and_saves()
    {
        var tenant = _fixture.GivenTenant();

        await new ActivateTenantHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new ActivateTenantCommand(tenant.Id.Value));

        Assert.Equal(TenantStatus.Active, tenant.Status);
        DomainAssert.Raised<TenantActivated>(tenant);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task Suspend_then_reactivate_then_close()
    {
        var tenant = _fixture.GivenTenant();

        await new SuspendTenantHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new SuspendTenantCommand(tenant.Id.Value, TenantSuspensionReason.PaymentOverdue));
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, tenant.SuspensionReason);

        await new ReactivateTenantHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new ReactivateTenantCommand(tenant.Id.Value));
        Assert.Equal(TenantStatus.Active, tenant.Status);

        await new CloseTenantHandler(_fixture, _fixture, _fixture.Clock)
            .HandleAsync(new CloseTenantCommand(tenant.Id.Value));
        Assert.Equal(TenantStatus.Closed, tenant.Status);
        Assert.Equal(3, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_broken_rule_propagates_and_nothing_is_saved()
    {
        var tenant = _fixture.GivenTenant();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => new ReactivateTenantHandler(_fixture, _fixture, _fixture.Clock)
                .HandleAsync(new ReactivateTenantCommand(tenant.Id.Value)));

        Assert.Equal(TenantErrors.NotSuspended.Code, exception.Code);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => new ActivateTenantHandler(_fixture, _fixture, _fixture.Clock)
                .HandleAsync(new ActivateTenantCommand(Guid.NewGuid())));

        Assert.Equal(TenantApplicationErrors.NotFound.Code, exception.Code);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_empty_tenant_id_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => new CloseTenantHandler(_fixture, _fixture, _fixture.Clock)
                .HandleAsync(new CloseTenantCommand(Guid.Empty)));

        Assert.Equal(CommonErrors.EmptyId.Code, exception.Code);
    }
}

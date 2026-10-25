using Billing.Application;
using Billing.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.BuildingBlocks.Domain;

namespace SaasRestaurant.Host.Tests;

public sealed class PlanFeatureGateTests
{
    private readonly TenantId _tenant = TenantId.New();

    private sealed class Context(TenantId? current) : ITenantContext
    {
        public TenantId? CurrentTenantId { get; } = current;
    }

    private sealed class Limits(bool enabled, bool subscribed) : IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto>
    {
        public int Calls { get; private set; }

        public Task<FeatureLimitDto> HandleAsync(GetFeatureLimitQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new FeatureLimitDto(query.FeatureCode, enabled, null, subscribed));
        }
    }

    [Fact]
    public async Task The_gate_follows_the_plan_of_the_restaurant_of_the_context()
    {
        Assert.True(await new PlanFeatureGateFromBilling(new Context(_tenant), new Limits(true, true)).AllowsAsync(_tenant, PlanFeatures.Loyalty));
        Assert.False(await new PlanFeatureGateFromBilling(new Context(_tenant), new Limits(false, true)).AllowsAsync(_tenant, PlanFeatures.Loyalty));
    }

    [Fact]
    public async Task The_gate_is_closed_without_a_live_subscription_or_for_another_tenant()
    {
        var other = new Limits(true, true);

        Assert.False(await new PlanFeatureGateFromBilling(new Context(_tenant), new Limits(true, false)).AllowsAsync(_tenant, PlanFeatures.Loyalty));
        Assert.False(await new PlanFeatureGateFromBilling(new Context(TenantId.New()), other).AllowsAsync(_tenant, PlanFeatures.Loyalty));
        Assert.False(await new PlanFeatureGateFromBilling(new Context(null), other).AllowsAsync(_tenant, BillingFeatures.StorefrontApi));
        Assert.Equal(0, other.Calls);
    }
}

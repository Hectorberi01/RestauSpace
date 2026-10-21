using Billing.Domain;

namespace Billing.Domain;

public class EntitlementTests
{
    private static readonly FeatureCode Orders = FeatureCode.Create("orders.monthly");

    [Theory]
    [InlineData("orders.monthly", "orders.monthly")]
    [InlineData("  Orders.Monthly ", "orders.monthly")]
    [InlineData("KDS", "kds")]
    [InlineData("api_calls-v2", "api_calls-v2")]
    public void FeatureCode_is_trimmed_and_lower_cased(string input, string expected)
    {
        var code = FeatureCode.Create(input);

        Assert.Equal(expected, code.Value);
        Assert.Equal(expected, code.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FeatureCode_is_required(string? input) =>
        DomainAssert.Throws(PlanErrors.FeatureCodeRequired, () => FeatureCode.Create(input));

    [Theory]
    [InlineData("a")]
    [InlineData("1orders")]
    [InlineData(".orders")]
    [InlineData("orders monthly")]
    [InlineData("commandes/mois")]
    [InlineData("réservations")]
    public void FeatureCode_rejects_invalid_formats(string input) =>
        DomainAssert.Throws(PlanErrors.FeatureCodeInvalid, () => FeatureCode.Create(input));

    [Fact]
    public void FeatureCode_length_is_bounded()
    {
        DomainAssert.Throws(PlanErrors.FeatureCodeInvalid, () => FeatureCode.Create(new string('a', FeatureCode.MaxLength + 1)));

        Assert.Equal(FeatureCode.MaxLength, FeatureCode.Create(new string('a', FeatureCode.MaxLength)).Value.Length);
        Assert.Equal(FeatureCode.Create("ORDERS.monthly"), Orders);
    }

    [Fact]
    public void Limited_entitlement_carries_its_limit()
    {
        var entitlement = Entitlement.Limited(Orders, 500);

        Assert.Equal(Orders, entitlement.FeatureCode);
        Assert.True(entitlement.Enabled);
        Assert.Equal(500, entitlement.Limit);
        Assert.False(entitlement.IsUnlimited);
    }

    [Fact]
    public void Limit_cannot_be_negative()
    {
        DomainAssert.Throws(PlanErrors.EntitlementLimitNegative, () => Entitlement.Limited(Orders, -1));
        DomainAssert.Throws(PlanErrors.EntitlementLimitNegative, () => Entitlement.Create(Orders, false, -1));
    }

    [Fact]
    public void Limit_of_zero_is_valid_and_allows_nothing()
    {
        var entitlement = Entitlement.Limited(Orders, 0);

        Assert.Equal(0, entitlement.Limit);
        Assert.True(entitlement.Allows(0));
        Assert.False(entitlement.Allows(1));
    }

    [Fact]
    public void Null_limit_means_unlimited()
    {
        var entitlement = Entitlement.Unlimited(Orders);

        Assert.True(entitlement.Enabled);
        Assert.Null(entitlement.Limit);
        Assert.True(entitlement.IsUnlimited);
        Assert.True(entitlement.Allows(long.MaxValue));
    }

    [Fact]
    public void Disabled_entitlement_cannot_carry_a_limit()
    {
        DomainAssert.Throws(PlanErrors.EntitlementDisabledWithLimit, () => Entitlement.Create(Orders, enabled: false, limit: 10));
        DomainAssert.Throws(PlanErrors.EntitlementDisabledWithLimit, () => Entitlement.Create(Orders, enabled: false, limit: 0));
    }

    [Fact]
    public void Disabled_entitlement_allows_nothing()
    {
        var entitlement = Entitlement.Disabled(Orders);

        Assert.False(entitlement.Enabled);
        Assert.Null(entitlement.Limit);
        Assert.False(entitlement.IsUnlimited);
        Assert.False(entitlement.Allows(0));
    }

    [Fact]
    public void Allows_is_inclusive_of_the_limit()
    {
        var entitlement = Entitlement.Limited(Orders, 3);

        Assert.True(entitlement.Allows(3));
        Assert.False(entitlement.Allows(4));
    }

    [Fact]
    public void Entitlements_are_compared_by_value()
    {
        Assert.Equal(Entitlement.Limited(Orders, 5), Entitlement.Create(FeatureCode.Create("ORDERS.MONTHLY"), true, 5));
        Assert.NotEqual(Entitlement.Limited(Orders, 5), Entitlement.Limited(Orders, 6));
        Assert.NotEqual(Entitlement.Unlimited(Orders), Entitlement.Disabled(Orders));
        Assert.NotEqual(Entitlement.Unlimited(Orders), Entitlement.Unlimited(FeatureCode.Create("kds")));
    }

    [Fact]
    public void Ids_reject_empty_guids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => PlanId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => SubscriptionId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => UsageCounterId.From(Guid.Empty));

        var guid = Guid.CreateVersion7();
        Assert.Equal(guid, PlanId.From(guid).Value);
        Assert.Equal(guid.ToString(), SubscriptionId.From(guid).ToString());
        Assert.NotEqual(UsageCounterId.New(), UsageCounterId.New());
    }
}

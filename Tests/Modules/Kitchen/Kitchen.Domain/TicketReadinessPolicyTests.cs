namespace Kitchen.Domain;

public class TicketReadinessPolicyTests
{
    [Fact]
    public void AllItemsReady_requires_every_item()
    {
        var policy = TicketReadinessPolicy.AllItemsReady;

        Assert.Equal(100, policy.RequiredReadyPercentage);
        Assert.False(policy.IsSatisfiedBy(0, 3));
        Assert.False(policy.IsSatisfiedBy(2, 3));
        Assert.True(policy.IsSatisfiedBy(3, 3));
    }

    [Fact]
    public void AtLeast_is_satisfied_from_the_required_share()
    {
        var policy = TicketReadinessPolicy.AtLeast(50);

        Assert.False(policy.IsSatisfiedBy(1, 3));
        Assert.True(policy.IsSatisfiedBy(1, 2));
        Assert.True(policy.IsSatisfiedBy(2, 3));
    }

    [Fact]
    public void No_policy_is_satisfied_without_a_ready_item_or_without_items()
    {
        Assert.False(TicketReadinessPolicy.AtLeast(1).IsSatisfiedBy(0, 5));
        Assert.True(TicketReadinessPolicy.AtLeast(1).IsSatisfiedBy(1, 5));
        Assert.False(TicketReadinessPolicy.AtLeast(1).IsSatisfiedBy(0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void AtLeast_rejects_a_percentage_outside_1_to_100(int percentage) =>
        DomainAssert.Throws(KitchenTicketErrors.ReadinessPercentageOutOfRange, () => TicketReadinessPolicy.AtLeast(percentage));

    [Fact]
    public void Policies_with_the_same_threshold_are_equal()
    {
        Assert.Equal(TicketReadinessPolicy.AllItemsReady, TicketReadinessPolicy.AtLeast(100));
        Assert.NotEqual(TicketReadinessPolicy.AllItemsReady, TicketReadinessPolicy.AtLeast(99));
    }
}

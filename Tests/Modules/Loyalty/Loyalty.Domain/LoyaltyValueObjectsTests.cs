using Loyalty.Domain;

namespace Loyalty.Domain;

public class LoyaltyValueObjectsTests
{
    [Fact]
    public void Points_reject_a_negative_value() =>
        DomainAssert.Throws(LoyaltyAccountErrors.PointsNegative, () => Points.Of(-1));

    [Fact]
    public void Points_accept_zero_and_positive_values()
    {
        Assert.True(Points.Of(0).IsZero);
        Assert.Equal(Points.Zero, Points.Of(0));
        Assert.False(Points.Of(1).IsZero);
        Assert.Equal(250, Points.Of(250).Value);
        Assert.Equal("250", Points.Of(250).ToString());
    }

    [Fact]
    public void Points_are_compared_by_value()
    {
        Assert.Equal(Points.Of(10), Points.Of(10));
        Assert.NotEqual(Points.Of(10), Points.Of(11));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Reference_is_required(string? value) =>
        DomainAssert.Throws(LoyaltyAccountErrors.ReferenceRequired, () => TransactionReference.Create(value));

    [Fact]
    public void Reference_length_is_bounded()
    {
        DomainAssert.Throws(
            LoyaltyAccountErrors.ReferenceTooLong,
            () => TransactionReference.Create(new string('x', TransactionReference.MaxLength + 1)));

        Assert.Equal(
            TransactionReference.MaxLength,
            TransactionReference.Create(new string('x', TransactionReference.MaxLength)).Value.Length);
    }

    [Fact]
    public void Reference_is_trimmed_and_case_sensitive()
    {
        Assert.Equal("order-1", TransactionReference.Create("  order-1 ").Value);
        Assert.Equal("order-1", TransactionReference.Create("order-1").ToString());
        Assert.Equal(TransactionReference.Create("order-1"), TransactionReference.Create(" order-1"));
        Assert.NotEqual(TransactionReference.Create("order-1"), TransactionReference.Create("ORDER-1"));
    }

    [Fact]
    public void Overdraft_none_never_lets_the_balance_go_below_zero()
    {
        Assert.True(OverdraftPolicy.None.Allows(10, Points.Of(10)));
        Assert.False(OverdraftPolicy.None.Allows(10, Points.Of(11)));
        Assert.False(OverdraftPolicy.None.Allows(0, Points.Of(1)));
        Assert.Equal(Points.Zero, OverdraftPolicy.None.MaxOverdraft);
    }

    [Fact]
    public void Overdraft_up_to_allows_a_bounded_negative_balance()
    {
        var policy = OverdraftPolicy.UpTo(Points.Of(50));

        Assert.True(policy.Allows(10, Points.Of(60)));
        Assert.False(policy.Allows(10, Points.Of(61)));
        Assert.True(policy.Allows(-49, Points.Of(1)));
        Assert.False(policy.Allows(-50, Points.Of(1)));
        Assert.Equal(OverdraftPolicy.UpTo(Points.Of(50)), policy);
        Assert.NotEqual(OverdraftPolicy.None, policy);
    }

    [Fact]
    public void Overdraft_up_to_rejects_a_zero_allowance() =>
        DomainAssert.Throws(LoyaltyAccountErrors.OverdraftNotPositive, () => OverdraftPolicy.UpTo(Points.Zero));

    [Fact]
    public void Ids_reject_empty_guids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => LoyaltyAccountId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => LoyaltyTransactionId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => RewardId.From(Guid.Empty));

        var guid = Guid.CreateVersion7();
        Assert.Equal(guid, LoyaltyAccountId.From(guid).Value);
        Assert.Equal(guid.ToString(), RewardId.From(guid).ToString());
        Assert.NotEqual(LoyaltyTransactionId.New(), LoyaltyTransactionId.New());
    }
}

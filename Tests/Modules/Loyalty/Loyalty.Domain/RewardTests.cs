using Loyalty.Domain;

namespace Loyalty.Domain;

public class RewardTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private Reward NewReward(int cost = 100)
    {
        var reward = Reward.Create(Tenant, "Free coffee", Points.Of(cost), Eur(2.5m), _clock);
        reward.ClearDomainEvents();
        return reward;
    }

    [Fact]
    public void Create_starts_active_with_its_cost()
    {
        var reward = Reward.Create(Tenant, "  Free coffee ", Points.Of(100), Eur(2.5m), _clock);

        Assert.Equal(Tenant, reward.TenantId);
        Assert.Equal("Free coffee", reward.Name);
        Assert.Equal(Points.Of(100), reward.PointCost);
        Assert.Equal(Eur(2.5m), reward.DiscountValue);
        Assert.Equal(RewardStatus.Active, reward.Status);

        var created = DomainAssert.Raised<RewardCreated>(reward);
        Assert.Equal(
            (Tenant, reward.Id, "Free coffee", Points.Of(100), Eur(2.5m), FakeClock.DefaultNow),
            (created.TenantId, created.RewardId, created.Name, created.PointCost, created.DiscountValue, created.OccurredAtUtc));
    }

    [Fact]
    public void Create_rejects_a_zero_cost() =>
        DomainAssert.Throws(RewardErrors.CostNotPositive, () => Reward.Create(Tenant, "Free coffee", Points.Zero, Eur(2.5m), _clock));

    [Fact]
    public void Create_accepts_the_smallest_cost() =>
        Assert.Equal(1, Reward.Create(Tenant, "Sticker", Points.Of(1), Eur(2.5m), _clock).PointCost.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_a_missing_name(string? name) =>
        DomainAssert.Throws(RewardErrors.NameRequired, () => Reward.Create(Tenant, name!, Points.Of(10), Eur(2.5m), _clock));

    [Fact]
    public void Create_rejects_a_too_long_name() =>
        DomainAssert.Throws(
            RewardErrors.NameTooLong,
            () => Reward.Create(Tenant, new string('n', Reward.MaxNameLength + 1), Points.Of(10), Eur(2.5m), _clock));

    [Fact]
    public void Deactivate_then_activate_toggle_the_status()
    {
        var reward = NewReward();

        reward.Deactivate(_clock);
        Assert.Equal(RewardStatus.Inactive, reward.Status);
        Assert.Equal((Tenant, reward.Id), (DomainAssert.Raised<RewardDeactivated>(reward).TenantId, DomainAssert.Raised<RewardDeactivated>(reward).RewardId));

        reward.Activate(_clock);
        Assert.Equal(RewardStatus.Active, reward.Status);
        Assert.Equal(reward.Id, DomainAssert.Raised<RewardActivated>(reward).RewardId);
    }

    [Fact]
    public void Activate_rejects_an_already_active_reward()
    {
        var reward = NewReward();

        DomainAssert.Throws(RewardErrors.AlreadyActive, () => reward.Activate(_clock));

        DomainAssert.NoEvents(reward);
    }

    [Fact]
    public void Deactivate_rejects_an_already_inactive_reward()
    {
        var reward = NewReward();
        reward.Deactivate(_clock);
        reward.ClearDomainEvents();

        DomainAssert.Throws(RewardErrors.AlreadyInactive, () => reward.Deactivate(_clock));

        DomainAssert.NoEvents(reward);
    }

    [Fact]
    public void ChangeCost_updates_the_cost()
    {
        var reward = NewReward(cost: 100);

        reward.ChangeCost(Points.Of(150), _clock);

        Assert.Equal(Points.Of(150), reward.PointCost);
        var changed = DomainAssert.Raised<RewardCostChanged>(reward);
        Assert.Equal((Tenant, reward.Id, Points.Of(100), Points.Of(150)), (changed.TenantId, changed.RewardId, changed.PreviousCost, changed.NewCost));
    }

    [Fact]
    public void ChangeCost_rejects_a_zero_cost()
    {
        var reward = NewReward(cost: 100);

        DomainAssert.Throws(RewardErrors.CostNotPositive, () => reward.ChangeCost(Points.Zero, _clock));

        Assert.Equal(Points.Of(100), reward.PointCost);
        DomainAssert.NoEvents(reward);
    }

    [Fact]
    public void ChangeCost_to_the_same_cost_is_a_silent_no_op()
    {
        var reward = NewReward(cost: 100);

        reward.ChangeCost(Points.Of(100), _clock);

        Assert.Equal(Points.Of(100), reward.PointCost);
        DomainAssert.NoEvents(reward);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_non_positive_discount_value(decimal value) =>
        DomainAssert.Throws(
            RewardErrors.DiscountValueNotPositive,
            () => Reward.Create(Tenant, "Free coffee", Points.Of(100), Eur(value), _clock));

    [Fact]
    public void ChangeDiscountValue_updates_the_value()
    {
        var reward = NewReward();

        reward.ChangeDiscountValue(Eur(4m), _clock);

        Assert.Equal(Eur(4m), reward.DiscountValue);
        var changed = DomainAssert.Raised<RewardDiscountValueChanged>(reward);
        Assert.Equal(
            (Tenant, reward.Id, Eur(2.5m), Eur(4m), FakeClock.DefaultNow),
            (changed.TenantId, changed.RewardId, changed.PreviousValue, changed.NewValue, changed.OccurredAtUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ChangeDiscountValue_rejects_a_non_positive_value(decimal value)
    {
        var reward = NewReward();

        DomainAssert.Throws(RewardErrors.DiscountValueNotPositive, () => reward.ChangeDiscountValue(Eur(value), _clock));

        Assert.Equal(Eur(2.5m), reward.DiscountValue);
        DomainAssert.NoEvents(reward);
    }

    [Fact]
    public void ChangeDiscountValue_to_the_same_value_is_a_silent_no_op()
    {
        var reward = NewReward();

        reward.ChangeDiscountValue(Eur(2.5m), _clock);

        Assert.Equal(Eur(2.5m), reward.DiscountValue);
        DomainAssert.NoEvents(reward);
    }

    [Fact]
    public void ChangeDiscountValue_is_allowed_while_inactive()
    {
        var reward = NewReward();
        reward.Deactivate(_clock);

        reward.ChangeDiscountValue(Eur(1m), _clock);

        Assert.Equal(Eur(1m), reward.DiscountValue);
    }

    [Fact]
    public void ChangeCost_is_allowed_while_inactive()
    {
        var reward = NewReward(cost: 100);
        reward.Deactivate(_clock);

        reward.ChangeCost(Points.Of(80), _clock);

        Assert.Equal(Points.Of(80), reward.PointCost);
    }

    [Fact]
    public void ToReference_reflects_cost_and_availability()
    {
        var reward = NewReward(cost: 100);
        var active = reward.ToReference();
        Assert.Equal(
            (Tenant, reward.Id, Points.Of(100), Eur(2.5m), true),
            (active.TenantId, active.RewardId, active.PointCost, active.DiscountValue, active.IsActive));
        Assert.Equal(active, reward.ToReference());

        reward.Deactivate(_clock);
        reward.ChangeCost(Points.Of(70), _clock);
        reward.ChangeDiscountValue(Eur(1.5m), _clock);
        var inactive = reward.ToReference();
        Assert.Equal(
            (Tenant, reward.Id, Points.Of(70), Eur(1.5m), false),
            (inactive.TenantId, inactive.RewardId, inactive.PointCost, inactive.DiscountValue, inactive.IsActive));
    }

    [Fact]
    public void Reward_reference_cannot_be_built_outside_the_module()
    {
        Assert.DoesNotContain(typeof(RewardReference).GetConstructors(), constructor => constructor.IsPublic);
        Assert.All(typeof(RewardReference).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Reward.Create(default, "Free coffee", Points.Of(100), Eur(2.5m), _clock));

    [Fact]
    public void Reference_exposes_an_order_discount_reference_within_64_characters()
    {
        var reference = NewReward().ToReference();

        Assert.Equal(reference.RewardId.ToString(), reference.DiscountReference);
        Assert.InRange(reference.DiscountReference.Length, 1, 64);
    }

    [Fact]
    public void Reward_reference_can_be_redeemed_by_an_account_of_the_same_tenant()
    {
        var reward = NewReward(cost: 100);
        var account = LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, CustomerId.New(), false), true, _clock);
        account.Earn(Points.Of(120), TransactionReference.Create("order-1"), false, _clock);

        account.RedeemReward(reward.ToReference(), TransactionReference.Create("redemption-1"), false, _clock);

        Assert.Equal(20, account.Balance);
        Assert.Equal(reward.Id, DomainAssert.Raised<PointsRedeemed>(account).RewardId);
        var redeemed = DomainAssert.Raised<RewardRedeemed>(account);
        Assert.Equal((reward.Id, Eur(2.5m), reward.Id.ToString()), (redeemed.RewardId, redeemed.DiscountValue, redeemed.DiscountReference));
    }
}

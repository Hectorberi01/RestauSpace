using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class FreePlanTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    [Fact]
    public async Task The_free_plan_lasts_one_month_is_announced_then_ends_suspended()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(free));
        var started = Clock.UtcNow;
        var due = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());

        Clock.Set(started.AddMonths(1).AddDays(-6));
        var noticed = await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(id));
        Clock.Set(started.AddMonths(1).AddMinutes(1));
        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var subscription = _fixture.SubscriptionById(id);

        Assert.False(due.Due);
        Assert.Equal(started.AddMonths(1), subscription.CurrentPeriod.EndUtc);
        Assert.True(noticed);
        Assert.Equal((SubscriptionDueOutcome.TrialExpired, SubscriptionStatus.Suspended), (outcome, subscription.Status));
        Assert.Empty(_fixture.Payments);
    }

    [Fact]
    public async Task After_the_free_month_the_restaurant_can_still_choose_a_paying_plan()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var paying = await _fixture.PlanAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(free));
        Clock.Advance(TimeSpan.FromDays(32));
        await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        _fixture.Writable[_fixture.TenantA] = false;

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(paying));
        var back = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(free)));

        Assert.Equal(paying, _fixture.SubscriptionById(id).PlanId.Value);
        Assert.Equal(BillingApplicationErrors.FreePlanUnavailable, back.Error);
        Assert.True((await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery())).Due);
    }

    [Fact]
    public async Task The_free_plan_is_available_only_once()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var paying = await _fixture.PlanAsync();
        var first = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(paying));
        await _fixture.AdminCancel.HandleAsync(new AdminCancelSubscriptionCommand(first));

        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.Start.HandleAsync(new StartSubscriptionCommand(free)));
        var second = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(paying));
        var switched = await Assert.ThrowsAsync<DomainException>(() => _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(free)));

        Assert.Equal(BillingApplicationErrors.FreePlanUnavailable, again.Error);
        Assert.Equal(BillingApplicationErrors.FreePlanUnavailable, switched.Error);
        Assert.Equal(paying, _fixture.SubscriptionById(second).PlanId.Value);
    }

    [Fact]
    public async Task A_first_trial_can_switch_to_the_free_plan_within_its_first_month()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var paying = await _fixture.PlanAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(paying));

        await _fixture.ChangePlan.HandleAsync(new ChangeSubscriptionPlanCommand(free));

        Assert.Equal(free, _fixture.SubscriptionById(id).PlanId.Value);
        Clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal(SubscriptionDueOutcome.TrialExpired, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
    }

    [Fact]
    public async Task A_paying_plan_still_ends_its_trial_suspended()
    {
        var plan = await _fixture.PlanAsync();
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
        Clock.Advance(TimeSpan.FromDays(15));

        Assert.Equal(SubscriptionDueOutcome.TrialExpired, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
    }
}

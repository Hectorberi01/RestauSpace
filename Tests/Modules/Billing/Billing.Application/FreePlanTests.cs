using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class FreePlanTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    [Fact]
    public async Task A_free_plan_renews_itself_at_the_end_of_its_trial_without_notice_nor_payment()
    {
        var free = await _fixture.PlanAsync("Découverte", 0m);
        var id = await _fixture.Start.HandleAsync(new StartSubscriptionCommand(free));
        var due = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());

        Clock.Advance(TimeSpan.FromDays(8));
        var noticed = await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(id));
        Clock.Advance(TimeSpan.FromDays(7));
        var outcome = await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id));
        var subscription = _fixture.SubscriptionById(id);

        Assert.False(due.Due);
        Assert.False(noticed);
        DomainAssert.NotRaised<SubscriptionEndingSoon>(subscription);
        Assert.Equal((SubscriptionDueOutcome.RenewedFree, SubscriptionStatus.Active), (outcome, subscription.Status));
        Assert.True(subscription.CurrentPeriod.EndUtc > Clock.UtcNow);
        Assert.Empty(_fixture.Payments);

        Clock.Set(subscription.CurrentPeriod.EndUtc.AddMinutes(1));
        Assert.Equal(SubscriptionDueOutcome.RenewedFree, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));
        Assert.Equal(SubscriptionStatus.Active, _fixture.SubscriptionById(id).Status);
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

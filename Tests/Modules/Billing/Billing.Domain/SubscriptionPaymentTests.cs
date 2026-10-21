namespace Billing.Domain;

public class SubscriptionPaymentTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;
    private static readonly Period Trial = Period.Create(Now, Now.AddDays(14));
    private static readonly Period FirstMonth = Period.Create(Now.AddDays(14), Now.AddDays(44));

    private readonly FakeClock _clock = new();
    private readonly Plan _plan = Plan.Create("Starter", Money.Of(19m, Currency.EUR), BillingInterval.Monthly, [], new FakeClock());

    private SaasSubscription Trialing() =>
        SaasSubscription.StartTrial(Tenant, _plan.ToReference(), Trial, tenantHasNoLiveSubscription: true, _clock);

    [Fact]
    public void A_payment_copies_the_plan_price_and_the_paid_period()
    {
        var subscription = Trialing();
        subscription.Activate(FirstMonth, _clock);

        var payment = SubscriptionPayment.Record(subscription, _plan, _clock);

        Assert.Equal(
            (Tenant, subscription.Id, _plan.Id, "Starter", Money.Of(19m, Currency.EUR), FirstMonth, Now),
            (payment.TenantId, payment.SubscriptionId, payment.PlanId, payment.PlanName, payment.Amount, payment.PaidPeriod, payment.RecordedAtUtc));
        var recorded = DomainAssert.Raised<SubscriptionPaymentRecorded>(payment);
        Assert.Equal((payment.Id, FirstMonth), (recorded.PaymentId, recorded.PaidPeriod));
    }

    [Fact]
    public void A_payment_is_refused_while_the_subscription_is_not_active()
    {
        var refused = Assert.Throws<DomainException>(() => SubscriptionPayment.Record(Trialing(), _plan, _clock));

        Assert.Equal(SubscriptionPaymentErrors.SubscriptionNotActive, refused.Error);
    }

    [Fact]
    public void A_payment_is_refused_at_the_price_of_another_plan()
    {
        var subscription = Trialing();
        subscription.Activate(FirstMonth, _clock);
        var other = Plan.Create("Pro", Money.Of(49m, Currency.EUR), BillingInterval.Monthly, [], new FakeClock());

        var refused = Assert.Throws<DomainException>(() => SubscriptionPayment.Record(subscription, other, _clock));

        Assert.Equal(SubscriptionPaymentErrors.PlanMismatch, refused.Error);
    }
}

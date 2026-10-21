namespace Billing.Domain;

public class SubscriptionCheckoutTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;

    private readonly FakeClock _clock = new();
    private readonly Plan _plan = Plan.Create("Starter", Money.Of(19m, Currency.EUR), BillingInterval.Monthly, [], new FakeClock());

    private SaasSubscription Trialing() =>
        SaasSubscription.StartTrial(Tenant, _plan.ToReference(), Period.Create(Now, Now.AddDays(14)), tenantHasNoLiveSubscription: true, _clock);

    private SubscriptionCheckout Started() => SubscriptionCheckout.Start(Trialing(), Money.Of(19m, Currency.EUR), " FedaPay ", _clock);

    [Fact]
    public void A_checkout_starts_pending_for_thirty_minutes_and_is_paid_once_the_provider_reference_is_known()
    {
        var checkout = Started();
        var early = Assert.Throws<DomainException>(() => checkout.MarkPaid(null, _clock));
        checkout.AttachReference("12345", new Uri("https://checkout.fedapay.com/abc"));
        var payment = SubscriptionPaymentId.New();
        checkout.MarkPaid(payment, _clock);

        Assert.Equal(SubscriptionCheckoutErrors.ReferenceMissing, early.Error);
        Assert.Equal(("fedapay", Now.AddMinutes(30), "https://checkout.fedapay.com/abc"), (checkout.Provider, checkout.ExpiresAtUtc, checkout.CheckoutUrl));
        Assert.Equal((SubscriptionCheckoutStatus.Paid, payment, Now), (checkout.Status, checkout.PaymentId, checkout.ClosedAtUtc));
        Assert.Equal(payment, DomainAssert.Raised<SubscriptionCheckoutPaid>(checkout).PaymentId);
        Assert.Equal(SubscriptionCheckoutErrors.NotPending, Assert.Throws<DomainException>(() => checkout.Fail(_clock)).Error);
    }

    [Fact]
    public void A_checkout_expires_only_after_its_lifetime_and_refuses_an_insecure_page()
    {
        var checkout = Started();
        var insecure = Assert.Throws<DomainException>(() => checkout.AttachReference("1", new Uri("http://pay.example.com")));
        var tooEarly = Assert.Throws<DomainException>(() => checkout.Expire(_clock));
        _clock.Advance(SubscriptionCheckout.Lifetime);
        checkout.Expire(_clock);

        Assert.Equal(SubscriptionCheckoutErrors.CheckoutUrlInvalid, insecure.Error);
        Assert.Equal(SubscriptionCheckoutErrors.NotExpired, tooEarly.Error);
        Assert.Equal(SubscriptionCheckoutStatus.Expired, checkout.Status);
        Assert.False(checkout.IsOpenAt(_clock.UtcNow));
    }

    [Fact]
    public void A_checkout_needs_a_positive_amount_and_a_provider()
    {
        Assert.Equal(SubscriptionCheckoutErrors.AmountInvalid,
            Assert.Throws<DomainException>(() => SubscriptionCheckout.Start(Trialing(), Money.Of(0m, Currency.EUR), "fedapay", _clock)).Error);
        Assert.Equal(SubscriptionCheckoutErrors.ProviderInvalid,
            Assert.Throws<DomainException>(() => SubscriptionCheckout.Start(Trialing(), Money.Of(5m, Currency.EUR), " ", _clock)).Error);
    }

    [Fact]
    public void An_invoice_number_is_given_once_in_the_yearly_format()
    {
        var subscription = Trialing();
        subscription.Activate(Period.Create(Now.AddDays(14), Now.AddDays(44)), _clock);
        var payment = SubscriptionPayment.Record(subscription, _plan, [], "fedapay", _clock);
        payment.AssignInvoiceNumber(SubscriptionPayment.FormatInvoiceNumber(2026, 42));

        Assert.Equal(("F2026-000042", "fedapay"), (payment.InvoiceNumber, payment.CollectedBy));
        Assert.Equal(SubscriptionPaymentErrors.InvoiceNumberAlreadySet,
            Assert.Throws<DomainException>(() => payment.AssignInvoiceNumber("F2026-000043")).Error);
    }
}

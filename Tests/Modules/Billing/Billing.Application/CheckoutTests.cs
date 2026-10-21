using Billing.Application.Abstractions;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class CheckoutTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    private async Task<Guid> TrialAsync() =>
        await _fixture.Start.HandleAsync(new StartSubscriptionCommand(await _fixture.PlanAsync()));

    [Fact]
    public async Task A_trial_is_paid_online_once_the_provider_confirms_and_the_payment_gets_an_invoice_number()
    {
        var id = await TrialAsync();
        var trialEnd = _fixture.SubscriptionById(id).CurrentPeriod.EndUtc;

        var due = await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery());
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand("+22997000001"));
        var waiting = await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id));
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, 29m);
        var paid = await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id));

        Assert.Equal((true, true, 29m, "EUR"), (due.Available, due.Due, due.Amount, due.CurrencyCode));
        Assert.Equal((SubscriptionCheckoutStatus.Pending, "https://pay.example.com/1"), (started.Status, started.CheckoutUrl));
        Assert.Equal(("Abonnement Essentiel", "+22997000001"), (_fixture.Started.Single().Label, _fixture.Started.Single().Phone));
        Assert.Equal(SubscriptionCheckoutStatus.Pending, waiting.Status);
        Assert.Equal(SubscriptionCheckoutStatus.Paid, paid.Status);
        var subscription = _fixture.SubscriptionById(id);
        Assert.Equal((SubscriptionStatus.Active, trialEnd, trialEnd.AddMonths(1)), (subscription.Status, subscription.CurrentPeriod.StartUtc, subscription.CurrentPeriod.EndUtc));
        var payment = Assert.Single(_fixture.Payments);
        Assert.Equal((paid.PaymentId, "fedapay", $"F{payment.RecordedAtUtc.Year}-000001"), (payment.Id.Value, payment.CollectedBy, payment.InvoiceNumber));
        Assert.False((await _fixture.OnlinePayment.HandleAsync(new GetOnlinePaymentQuery())).Due);
    }

    [Fact]
    public async Task An_open_checkout_is_reused_and_a_refused_one_lets_the_restaurant_try_again()
    {
        await TrialAsync();

        var first = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        var again = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Failed, null);
        var retry = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, retry.Id);
        Assert.Equal(SubscriptionCheckoutStatus.Failed, _fixture.Checkouts.Single(checkout => checkout.Id.Value == first.Id).Status);
        Assert.Equal(2, _fixture.Started.Count);
    }

    [Fact]
    public async Task Nothing_is_started_without_a_provider_while_a_period_is_paid_or_when_cancellation_is_scheduled()
    {
        await TrialAsync();
        _fixture.CollectorProvider = null;
        var unavailable = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand()));

        _fixture.CollectorProvider = "fedapay";
        await _fixture.ScheduleCancellation.HandleAsync(new ScheduleSubscriptionCancellationCommand());
        var scheduled = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand()));
        await _fixture.UnscheduleCancellation.HandleAsync(new UnscheduleSubscriptionCancellationCommand());

        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, null);
        await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id));
        var paidUp = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand()));

        Assert.Equal(BillingApplicationErrors.OnlinePaymentUnavailable, unavailable.Error);
        Assert.Equal(BillingApplicationErrors.CancellationScheduled, scheduled.Error);
        Assert.Equal(BillingApplicationErrors.NothingToPay, paidUp.Error);
    }

    [Fact]
    public async Task A_provider_failure_closes_the_checkout_and_an_unanswered_one_expires()
    {
        await TrialAsync();
        _fixture.ProviderDown = true;
        await Assert.ThrowsAsync<DomainException>(() => _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand()));
        Assert.Equal(SubscriptionCheckoutStatus.Failed, Assert.Single(_fixture.Checkouts).Status);

        _fixture.ProviderDown = false;
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        Clock.Advance(SubscriptionCheckout.Lifetime);
        var status = await _fixture.SettleCheckout.HandleAsync(new SettleSubscriptionCheckoutCommand(_fixture.TenantA.Value, started.Id));

        Assert.Equal(SubscriptionCheckoutStatus.Expired, status);
        Assert.Empty(_fixture.Payments);
    }

    [Fact]
    public async Task Money_received_for_a_period_paid_meanwhile_or_short_of_the_amount_is_kept_without_a_payment()
    {
        var id = await TrialAsync();
        var first = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        _fixture.CurrentTenantId = null;
        await _fixture.RecordPayment.HandleAsync(new RecordSubscriptionPaymentCommand(id));
        _fixture.CurrentTenantId = _fixture.TenantA;
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, 29m);

        var late = await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(first.Id));

        Assert.Equal((SubscriptionCheckoutStatus.Paid, (Guid?)null), (late.Status, late.PaymentId));
        Assert.Single(_fixture.Payments);

        var other = new BillingFixture();
        await other.Start.HandleAsync(new StartSubscriptionCommand(await other.PlanAsync()));
        var short_ = await other.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        other.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, 10m);
        var underpaid = await other.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(short_.Id));
        Assert.Equal((SubscriptionCheckoutStatus.Paid, (Guid?)null), (underpaid.Status, underpaid.PaymentId));
        Assert.Empty(other.Payments);
    }

    [Fact]
    public async Task A_checkout_or_an_invoice_of_another_restaurant_is_not_found()
    {
        await TrialAsync();
        var started = await _fixture.StartCheckout.HandleAsync(new StartSubscriptionCheckoutCommand());
        _fixture.ProviderStatuses["ref-1"] = new CollectionStatus(CollectionState.Paid, null);
        var paid = await _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id));

        var invoice = await _fixture.Invoice.HandleAsync(new GetMyInvoiceQuery(paid.PaymentId!.Value));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var checkout = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RefreshCheckout.HandleAsync(new RefreshSubscriptionCheckoutCommand(started.Id)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Invoice.HandleAsync(new GetMyInvoiceQuery(paid.PaymentId!.Value)));

        Assert.Equal(("Chez A", "RestauSpace SAS", 29m, "fedapay"), (invoice.CustomerName, invoice.Issuer.Name, invoice.Total, invoice.CollectedBy));
        Assert.Matches("^F[0-9]{4}-000001$", invoice.Number);
        Assert.Equal("Essentiel", Assert.Single(invoice.Lines).Label);
        Assert.Equal(BillingApplicationErrors.CheckoutNotFound, checkout.Error);
        Assert.Equal(BillingApplicationErrors.InvoiceNotFound, foreign.Error);
    }
}

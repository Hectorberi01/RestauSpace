namespace Payments.Domain;

public class PaymentOfflineTests
{
    private static readonly ProviderReference Receipt = Given.Ref("TILL-2-000184");

    private static Payment CashPayment(Money? amount = null, IClock? clock = null) =>
        Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(amount ?? Given.Eur(100m)), amount ?? Given.Eur(100m), Receipt,
            clock ?? new FakeClock());

    [Fact]
    public void RecordOffline_creates_a_payment_already_captured_for_its_whole_amount()
    {
        var clock = new FakeClock();
        var order = Given.Order(Given.Xof(5000m));

        var payment = Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Xof(5000m), Receipt, clock);

        Assert.Equal(Given.Tenant, payment.TenantId);
        Assert.Equal(order.OrderId, payment.OrderId);
        Assert.Equal("cash", payment.Provider.Value);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Xof(5000m), payment.Amount);
        Assert.Equal(Given.Xof(5000m), payment.CapturedAmount);
        Assert.Equal(Given.Xof(5000m), payment.NetPaidAmount);
        Assert.Equal(Given.Xof(5000m), payment.RefundableAmount);
        Assert.Equal(clock.UtcNow, payment.CapturedAtUtc);
        Assert.False(payment.HasUnexpectedCapture);
        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(Receipt, attempt.ProviderReference);
        Assert.False(attempt.IsAuthorized);
    }

    [Fact]
    public void RecordOffline_raises_the_same_events_as_a_provider_payment_captured_at_once()
    {
        var clock = new FakeClock();

        var payment = CashPayment(clock: clock);

        Assert.Equal(
            [typeof(PaymentCreated), typeof(PaymentAttemptRegistered), typeof(PaymentCaptured)],
            payment.DomainEvents.Select(domainEvent => domainEvent.GetType()).ToArray());
        var captured = DomainAssert.Raised<PaymentCaptured>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId), (captured.TenantId, captured.PaymentId, captured.OrderId));
        Assert.Equal(Assert.Single(payment.Attempts).Id, captured.AttemptId);
        Assert.Equal(Receipt, captured.ProviderReference);
        Assert.Equal(Given.Eur(100m), captured.CapturedAmount);
        Assert.Equal(clock.UtcNow, captured.OccurredAtUtc);
        Assert.Equal(PaymentProviderCode.Cash, DomainAssert.Raised<PaymentCreated>(payment).Provider);
    }

    [Fact]
    public void RecordOffline_accepts_any_on_site_provider_of_the_restaurant()
    {
        var terminal = Given.OnSiteConfiguration("external_terminal", PaymentMethodType.CardOnSite);

        var payment = Payment.RecordOffline(
            terminal, PaymentMethodType.CardOnSite, Given.Order(Given.Eur(20m)), Given.Eur(20m), Given.Ref("TPE-7781"), new FakeClock());

        Assert.Equal(PaymentProviderCode.Create("external_terminal"), payment.Provider);
        Assert.Equal(PaymentMethodType.CardOnSite, payment.Method);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void RecordOffline_can_pay_only_a_part_of_what_is_due()
    {
        var payment = Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(90m)), Given.Eur(30m), Receipt, new FakeClock());

        Assert.Equal(Given.Eur(30m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(30m), payment.NetPaidAmount);
    }

    [Fact]
    public void RecordOffline_applies_the_rules_of_Create()
    {
        var clock = new FakeClock();
        var order = Given.Order(Given.Eur(50m));

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(50m), TenantId.New()), Given.Eur(50m), Receipt, clock));
        DomainAssert.Throws(
            PaymentErrors.CurrencyMismatch,
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Xof(50m), Receipt, clock));
        DomainAssert.Throws(
            PaymentErrors.AmountNotPositive,
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Eur(0m), Receipt, clock));
        DomainAssert.Throws(
            PaymentErrors.AmountExceedsAmountDue,
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Eur(50.01m), Receipt, clock));
    }

    [Fact]
    public void RecordOffline_requires_the_operator_reference()
    {
        var order = Given.Order(Given.Eur(50m));

        Assert.Throws<ArgumentNullException>(
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Eur(50m), null!, new FakeClock()));
        DomainAssert.Throws(PaymentErrors.ProviderReferenceInvalid, () => ProviderReference.Create("  "));
    }

    [Fact]
    public void Offline_payment_accepts_no_further_attempt_and_cannot_be_cancelled()
    {
        var clock = new FakeClock();
        var payment = CashPayment(clock: clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptNotAllowed, () => payment.RegisterAttempt(Given.Ref("TILL-2-000185"), clock));
        DomainAssert.Throws(PaymentErrors.CancelNotAllowed, () => payment.Cancel("Mistake", clock));
        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.Capture(Receipt, Given.Eur(50m), clock));

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Offline_payment_is_partially_then_fully_refunded_like_any_captured_payment()
    {
        var clock = new FakeClock();
        var payment = CashPayment(clock: clock);

        var first = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        Assert.Equal(Given.Eur(60m), payment.RefundableAmount);
        payment.RegisterRefund(first, RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(40m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(60m), payment.NetPaidAmount);

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(60.01m), "Too much", clock));
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(60m), "Order cancelled", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.NetPaidAmount);
        Assert.Equal(2, payment.DomainEvents.OfType<RefundSucceeded>().Count());
    }

    [Fact]
    public void Cash_is_a_valid_provider_code() => Assert.Equal(PaymentProviderCode.Create("cash"), PaymentProviderCode.Cash);
}

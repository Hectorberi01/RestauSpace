namespace Payments.Domain;

/// <summary>Identifiants vides, collections enfants bornées et un seul instant par opération.</summary>
public class PaymentHardeningTests
{
    [Fact]
    public void Create_rejects_an_order_of_another_restaurant_than_the_configuration()
    {
        var order = Given.Order(Given.Eur(100m), TenantId.New());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => Payment.Create(Given.Online, PaymentMethodType.Card, order, Given.Eur(100m), new FakeClock()));
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, order, Given.Eur(100m), Given.Ref("TILL-1"), new FakeClock()));
    }

    [Fact]
    public void Order_reference_rejects_an_empty_tenant_order_or_location()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderReference.Create(default, OrderId.New(), Given.Location, Given.Eur(100m)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderReference.Create(Given.Tenant, default, Given.Location, Given.Eur(100m)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderReference.Create(Given.Tenant, OrderId.New(), default, Given.Eur(100m)));
    }

    [Fact]
    public void Payment_accepts_the_maximum_number_of_attempts_and_rejects_one_more()
    {
        var clock = new FakeClock();
        var payment = Given.NewPayment(clock: clock);
        for (var i = 0; i < Payment.MaxAttempts; i++)
        {
            payment.RegisterAttempt(Given.Ref($"pi_{i}"), clock);
            payment.Fail(Given.Ref($"pi_{i}"), Given.Code("card_declined"), clock);
        }

        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.TooManyAttempts, () => payment.RegisterAttempt(Given.Ref("pi_one_too_many"), clock));

        Assert.Equal(Payment.MaxAttempts, payment.Attempts.Count);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        DomainAssert.NoEvents(payment);
        // L'issue reste ouverte : abandonner ce paiement et en démarrer un autre.
        payment.Cancel("Too many declined cards", clock);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void Payment_accepts_the_maximum_number_of_refunds_and_rejects_one_more()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        for (var i = 0; i < Payment.MaxRefunds; i++)
        {
            payment.RegisterRefund(payment.RequestRefund(Given.Eur(0.10m), "Gesture", clock), RefundOutcome.Succeeded, clock);
        }

        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.TooManyRefunds, () => payment.RequestRefund(Given.Eur(0.10m), "One too many", clock));

        Assert.Equal(Payment.MaxRefunds, payment.Refunds.Count);
        Assert.Equal(Given.Eur(100m - (Payment.MaxRefunds * 0.10m)), payment.RefundableAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_replayed_without_a_reason_is_absorbed_like_any_replay(string? reason)
    {
        var clock = new FakeClock();
        var failed = Given.PaymentIn(PaymentStatus.Failed, clock);
        var cancelled = Given.PaymentIn(PaymentStatus.Cancelled, clock);
        failed.ClearDomainEvents();
        cancelled.ClearDomainEvents();

        failed.Fail(Given.Ref("pi_1"), Given.Code("expired"), clock, reason);
        cancelled.Fail(Given.Ref("pi_1"), Given.Code("expired"), clock, reason);

        Assert.Equal(PaymentStatus.Failed, failed.Status);
        Assert.Equal(Given.Code("card_declined"), Assert.Single(failed.Attempts).FailureCode);
        Assert.Equal(PaymentStatus.Cancelled, cancelled.Status);
        DomainAssert.NoEvents(failed);
        DomainAssert.NoEvents(cancelled);
    }

    [Fact]
    public void A_too_long_reason_is_rejected_on_an_open_attempt_and_a_succeeded_one_cannot_fail()
    {
        var clock = new FakeClock();
        var open = Given.PaymentWithOpenAttempt(clock: clock);
        var captured = Given.CapturedPayment(clock);
        var tooLong = new string('r', PaymentAttempt.MaxFailureReasonLength + 1);

        DomainAssert.Throws(PaymentErrors.FailureReasonInvalid, () => open.Fail(Given.Ref("pi_1"), Given.Code("declined"), clock, tooLong));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => captured.Fail(Given.Ref("pi_1"), Given.Code("declined"), clock));
        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => open.Fail(Given.Ref("pi_unknown"), Given.Code("declined"), clock));
        Assert.Equal(PaymentStatus.Pending, open.Status);
    }

    [Fact]
    public void Offline_payment_is_created_attempted_and_captured_at_one_single_instant()
    {
        var payment = Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(100m)), Given.Eur(100m), Given.Ref("TILL-1"), new TickingClock());

        var attempt = Assert.Single(payment.Attempts);
        DateTimeOffset?[] instants =
        [
            payment.CreatedAtUtc, attempt.StartedAtUtc, attempt.CompletedAtUtc, payment.CapturedAtUtc,
            .. payment.DomainEvents.Select(domainEvent => (DateTimeOffset?)domainEvent.OccurredAtUtc),
        ];
        Assert.Equal(3, payment.DomainEvents.Count);
        Assert.Single(instants.Distinct());
    }

    [Fact]
    public void Timestamps_of_an_operation_equal_the_time_of_its_event()
    {
        var clock = new TickingClock();
        var payment = Given.NewPayment(clock: clock);
        Assert.Equal(payment.CreatedAtUtc, DomainAssert.Raised<PaymentCreated>(payment).OccurredAtUtc);

        payment.RegisterAttempt(Given.Ref("pi_1"), clock);
        Assert.Equal(payment.Attempts.Last().StartedAtUtc, payment.DomainEvents.OfType<PaymentAttemptRegistered>().Last().OccurredAtUtc);

        payment.Fail(Given.Ref("pi_1"), Given.Code("card_declined"), clock);
        Assert.Equal(payment.Attempts.Last().CompletedAtUtc, DomainAssert.Raised<PaymentFailed>(payment).OccurredAtUtc);

        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        payment.Authorize(Given.Ref("pi_2"), clock);
        Assert.Equal(payment.Attempts.Last().AuthorizedAtUtc, DomainAssert.Raised<PaymentAuthorized>(payment).OccurredAtUtc);

        payment.Capture(Given.Ref("pi_2"), Given.Eur(100m), clock);
        var captured = DomainAssert.Raised<PaymentCaptured>(payment);
        Assert.Equal((payment.CapturedAtUtc, payment.Attempts.Last().CompletedAtUtc), (captured.OccurredAtUtc, captured.OccurredAtUtc));

        var refundId = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(refund.RequestedAtUtc, DomainAssert.Raised<RefundRequested>(payment).OccurredAtUtc);

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);
        Assert.Equal(refund.CompletedAtUtc, DomainAssert.Raised<RefundRegistered>(payment).OccurredAtUtc);
        Assert.Equal(refund.CompletedAtUtc, DomainAssert.Raised<RefundSucceeded>(payment).OccurredAtUtc);
    }

    [Fact]
    public void Timestamps_of_a_cancellation_and_of_a_late_capture_equal_the_time_of_their_events()
    {
        var clock = new TickingClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        payment.Cancel("Order cancelled", clock);
        var cancelled = DomainAssert.Raised<PaymentCancelled>(payment);
        Assert.Equal((payment.CancelledAtUtc, Assert.Single(payment.Attempts).CompletedAtUtc), (cancelled.OccurredAtUtc, cancelled.OccurredAtUtc));

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        var late = DomainAssert.Raised<PaymentCapturedAfterCancellation>(payment);
        Assert.Equal(
            (payment.CapturedAtUtc, Assert.Single(payment.Attempts).CapturedAfterCancellationAtUtc),
            (late.OccurredAtUtc, late.OccurredAtUtc));
    }
}

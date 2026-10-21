namespace Payments.Domain;

/// <summary>« Webhook dupliqué sans double effet » : rejeu exact = no-op silencieux, contradiction = erreur.</summary>
public class PaymentWebhookIdempotenceTests
{
    [Fact]
    public void Duplicate_capture_webhook_has_no_second_effect()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);
        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        var capturedAt = payment.CapturedAtUtc;
        var attemptCompletedAt = Assert.Single(payment.Attempts).CompletedAtUtc;
        payment.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(10));

        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        payment.Capture(Given.Ref("pi_1"), Given.Eur(100.00m), clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(capturedAt, payment.CapturedAtUtc);
        Assert.Equal(attemptCompletedAt, Assert.Single(payment.Attempts).CompletedAtUtc);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_capture_webhook_raises_PaymentCaptured_exactly_once_overall()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        DomainAssert.Raised<PaymentCaptured>(payment);
    }

    [Fact]
    public void Capture_replayed_with_a_different_amount_is_a_conflict()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(99m), clock));

        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public void Duplicate_capture_webhook_arriving_after_a_refund_does_not_undo_the_refund(PaymentStatus status)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(status, clock);
        var refunded = payment.RefundedAmount;
        payment.ClearDomainEvents();

        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        Assert.Equal(status, payment.Status);
        Assert.Equal(refunded, payment.RefundedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_authorization_webhook_has_no_second_effect()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Authorized, clock);
        var authorizedAt = Assert.Single(payment.Attempts).AuthorizedAtUtc;
        payment.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(1));

        payment.Authorize(Given.Ref("pi_1"), clock);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(authorizedAt, Assert.Single(payment.Attempts).AuthorizedAtUtc);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Authorization_webhook_arriving_after_the_capture_does_not_move_the_payment_back()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        payment.ClearDomainEvents();

        payment.Authorize(Given.Ref("pi_1"), clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_failure_webhook_has_no_second_effect_and_keeps_the_first_code()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.ClearDomainEvents();

        payment.Fail(Given.Ref("pi_1"), Given.Code("insufficient_funds"), clock, "another wording");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(Given.Code("card_declined"), Assert.Single(payment.Attempts).FailureCode);
        Assert.Null(Assert.Single(payment.Attempts).FailureReason);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_failure_webhook_of_an_old_attempt_does_not_fail_the_retry_in_progress()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        payment.ClearDomainEvents();

        payment.Fail(Given.Ref("pi_1"), Given.Code("card_declined"), clock);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.True(payment.Attempts.Last().IsOpen);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_failure_webhook_on_a_cancelled_payment_changes_nothing()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Cancelled, clock);
        payment.ClearDomainEvents();

        payment.Fail(Given.Ref("pi_1"), Given.Code("expired"), clock);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public void Failure_reported_for_a_succeeded_attempt_is_rejected(PaymentStatus status)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(status, clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => payment.Fail(Given.Ref("pi_1"), Given.Code("declined"), clock));

        Assert.Equal(status, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(PaymentAttemptStatus.Succeeded, Assert.Single(payment.Attempts).Status);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Success_reported_for_a_failed_attempt_is_rejected()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(PaymentAttemptStatus.Failed, Assert.Single(payment.Attempts).Status);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_refund_success_refunds_the_money_only_once()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);
        var completedAt = Assert.Single(payment.Refunds).CompletedAtUtc;
        payment.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(3));

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(40m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(60m), payment.RefundableAmount);
        Assert.Equal(completedAt, Assert.Single(payment.Refunds).CompletedAtUtc);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_refund_failure_changes_nothing()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        payment.RegisterRefund(refundId, RefundOutcome.Failed, clock);
        payment.ClearDomainEvents();

        payment.RegisterRefund(refundId, RefundOutcome.Failed, clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Duplicate_refund_success_on_a_fully_refunded_payment_changes_nothing()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Refunded, clock);
        var refundId = Assert.Single(payment.Refunds).Id;
        payment.ClearDomainEvents();

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Contradictory_refund_outcome_is_rejected()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var succeeded = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        var failed = payment.RequestRefund(Given.Eur(10m), "Late", clock);
        payment.RegisterRefund(succeeded, RefundOutcome.Succeeded, clock);
        payment.RegisterRefund(failed, RefundOutcome.Failed, clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => payment.RegisterRefund(succeeded, RefundOutcome.Failed, clock));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => payment.RegisterRefund(failed, RefundOutcome.Succeeded, clock));

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(40m), payment.RefundedAmount);
        DomainAssert.NoEvents(payment);
    }
}

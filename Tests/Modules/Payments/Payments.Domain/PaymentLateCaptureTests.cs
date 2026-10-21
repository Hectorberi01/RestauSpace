namespace Payments.Domain;

public class PaymentLateCaptureTests
{
    [Fact]
    public void RegisterLateCapture_records_the_money_taken_on_a_cancelled_payment()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        var cancelledAt = payment.CancelledAtUtc;
        payment.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(7));

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        Assert.True(payment.HasUnexpectedCapture);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(clock.UtcNow, payment.CapturedAtUtc);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
        // L'annulation n'est pas réécrite, et la tentative reste l'échec sur lequel elle a été close.
        Assert.Equal(cancelledAt, payment.CancelledAtUtc);
        Assert.Equal("Order cancelled", payment.CancellationReason);
        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.True(attempt.WasCapturedAfterCancellation);
        Assert.Equal(clock.UtcNow, attempt.CapturedAfterCancellationAtUtc);
    }

    [Fact]
    public void RegisterLateCapture_raises_a_dedicated_event_and_no_PaymentCaptured()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.ClearDomainEvents();

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(80m), clock);

        var late = DomainAssert.Raised<PaymentCapturedAfterCancellation>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId), (late.TenantId, late.PaymentId, late.OrderId));
        Assert.Equal(Assert.Single(payment.Attempts).Id, late.AttemptId);
        Assert.Equal(Given.Ref("pi_1"), late.ProviderReference);
        Assert.Equal(Given.Eur(80m), late.CapturedAmount);
        Assert.Equal(clock.UtcNow, late.OccurredAtUtc);
        DomainAssert.NotRaised<PaymentCaptured>(payment);
        Assert.Single(payment.DomainEvents);
    }

    [Fact]
    public void Late_capture_does_not_count_as_paid_for_the_order()
    {
        var payment = Given.CancelledAfterAuthorization();

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), new FakeClock());

        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(0m), payment.NetPaidAmount);
    }

    [Fact]
    public void RegisterLateCapture_is_idempotent_on_replay()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        var capturedAt = payment.CapturedAtUtc;
        payment.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(3));

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(capturedAt, payment.CapturedAtUtc);
        Assert.Equal(capturedAt, Assert.Single(payment.Attempts).CapturedAfterCancellationAtUtc);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RegisterLateCapture_replayed_after_the_refund_does_not_undo_the_refund()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(100m), "Captured after cancellation", clock), RefundOutcome.Succeeded, clock);
        payment.ClearDomainEvents();

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RegisterLateCapture_replayed_with_another_amount_or_attempt_is_a_conflict()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        payment.Cancel("Order cancelled", clock);
        payment.RegisterLateCapture(Given.Ref("pi_2"), Given.Eur(100m), clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.RegisterLateCapture(Given.Ref("pi_2"), Given.Eur(99m), clock));
        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Single(payment.Attempts, attempt => attempt.WasCapturedAfterCancellation);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public void RegisterLateCapture_is_rejected_on_a_payment_that_is_not_cancelled(PaymentStatus status)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(status, clock);
        var captured = payment.CapturedAmount;
        payment.ClearDomainEvents();

        DomainAssert.Throws(
            PaymentErrors.LateCaptureNotAllowed,
            () => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(status, payment.Status);
        Assert.Equal(captured, payment.CapturedAmount);
        Assert.False(payment.HasUnexpectedCapture);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RegisterLateCapture_rejects_an_unknown_reference()
    {
        var clock = new FakeClock();
        var withAttempt = Given.CancelledAfterAuthorization(clock);
        var withoutAttempt = Given.NewPayment(clock: clock);
        withoutAttempt.Cancel("Customer left", clock);

        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => withAttempt.RegisterLateCapture(Given.Ref("pi_other"), Given.Eur(100m), clock));
        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => withoutAttempt.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(PaymentStatus.Cancelled, withAttempt.Status);
        Assert.False(withAttempt.HasUnexpectedCapture);
    }

    [Fact]
    public void RegisterLateCapture_rejects_an_invalid_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.CurrencyMismatch, () => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Xof(100m), clock));
        DomainAssert.Throws(PaymentErrors.CaptureAmountNotPositive, () => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(0m), clock));

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        Assert.False(payment.HasUnexpectedCapture);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RegisterLateCapture_records_what_the_provider_really_took_even_above_the_requested_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.ClearDomainEvents();

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(120m), clock);

        // La seule exception à captured <= amount : l'argent a été pris, il faut le savoir pour le rendre.
        Assert.Equal(Given.Eur(100m), payment.Amount);
        Assert.Equal(Given.Eur(120m), payment.CapturedAmount);
        Assert.True(payment.HasUnexpectedCapture);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(120m), DomainAssert.Raised<PaymentCapturedAfterCancellation>(payment).CapturedAmount);
        Assert.Equal(Given.Eur(0m), payment.NetPaidAmount);
        Assert.Equal(Given.Eur(0m), payment.SettledAmount);
        Assert.Equal(Given.Eur(120m), payment.RefundableAmount);
    }

    [Fact]
    public void Late_capture_above_the_requested_amount_is_fully_refundable_and_never_counts_as_paid()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(120m), clock);

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(120.01m), "Too much", clock));
        var refund = payment.RequestRefund(Given.Eur(120m), "Captured after cancellation", clock);
        Assert.Equal((Given.Eur(0m), Given.Eur(0m)), (payment.NetPaidAmount, payment.SettledAmount));

        payment.RegisterRefund(refund, RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(120m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        Assert.Equal((Given.Eur(0m), Given.Eur(0m)), (payment.NetPaidAmount, payment.SettledAmount));
        Assert.True(payment.DomainEvents.OfType<RefundSucceeded>().Single().IsFullRefund);
    }

    [Fact]
    public void Late_capture_above_the_requested_amount_replays_like_any_other()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(120m), clock);
        payment.ClearDomainEvents();

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(120m), clock);
        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(Given.Eur(120m), payment.CapturedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Normal_capture_still_rejects_more_than_the_requested_amount()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        DomainAssert.Throws(PaymentErrors.CaptureExceedsAmount, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(120m), clock));

        Assert.False(payment.HasUnexpectedCapture);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
    }

    [Fact]
    public void RegisterLateCapture_requires_reference_amount_and_clock()
    {
        var payment = Given.CancelledAfterAuthorization();

        Assert.Throws<ArgumentNullException>(() => payment.RegisterLateCapture(null!, Given.Eur(1m), new FakeClock()));
        Assert.Throws<ArgumentNullException>(() => payment.RegisterLateCapture(Given.Ref("pi_1"), null!, new FakeClock()));
        Assert.Throws<ArgumentNullException>(() => payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(1m), null!));
    }

    [Fact]
    public void Normal_capture_keeps_rejecting_a_cancelled_payment_even_after_the_late_capture_is_recorded()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        DomainAssert.NotRaised<PaymentCaptured>(payment);
    }

    [Fact]
    public void Late_capture_is_refunded_through_the_normal_refund_flow()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(100.01m), "Too much", clock));
        var partial = payment.RequestRefund(Given.Eur(30m), "Captured after cancellation", clock);
        payment.RegisterRefund(partial, RefundOutcome.Succeeded, clock);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(70m), payment.RefundableAmount);

        payment.RegisterRefund(payment.RequestRefund(Given.Eur(70m), "Captured after cancellation", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        Assert.Equal(Given.Eur(0m), payment.NetPaidAmount);
        Assert.True(payment.HasUnexpectedCapture);
        Assert.True(payment.DomainEvents.OfType<RefundSucceeded>().Last().IsFullRefund);
    }

    [Fact]
    public void Payment_with_a_late_capture_cannot_be_retried_or_cancelled_again()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);
        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptNotAllowed, () => payment.RegisterAttempt(Given.Ref("pi_2"), clock));
        DomainAssert.Throws(PaymentErrors.CancelNotAllowed, () => payment.Cancel("Again", clock));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Authorize(Given.Ref("pi_1"), clock));

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        DomainAssert.NoEvents(payment);
    }
}

public class PaymentNetPaidAmountTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, 0)]
    [InlineData(PaymentStatus.Authorized, 0)]
    [InlineData(PaymentStatus.Failed, 0)]
    [InlineData(PaymentStatus.Cancelled, 0)]
    [InlineData(PaymentStatus.Captured, 100)]
    [InlineData(PaymentStatus.PartiallyRefunded, 60)]
    [InlineData(PaymentStatus.Refunded, 0)]
    public void NetPaidAmount_is_captured_minus_succeeded_refunds(PaymentStatus status, int expected) =>
        Assert.Equal(Given.Eur(expected), Given.PaymentIn(status).NetPaidAmount);

    [Fact]
    public void NetPaidAmount_ignores_refunds_that_are_pending_or_failed()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);

        var pending = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);
        Assert.Equal(Given.Eur(100m), payment.NetPaidAmount);

        payment.RegisterRefund(pending, RefundOutcome.Failed, clock);
        Assert.Equal(Given.Eur(100m), payment.NetPaidAmount);
    }

    [Fact]
    public void NetPaidAmount_of_a_partial_capture_is_what_was_really_taken()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        payment.Capture(Given.Ref("pi_1"), Given.Eur(80m), clock);

        Assert.Equal(Given.Eur(80m), payment.NetPaidAmount);
    }

    [Fact]
    public void Split_bill_settlement_is_the_sum_of_the_net_paid_amounts_of_the_order_payments()
    {
        var clock = new FakeClock();
        var orderId = OrderId.New();
        var card = Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(90m), orderId: orderId), Given.Eur(60m), clock);
        card.RegisterAttempt(Given.Ref("pi_1"), clock);
        card.Capture(Given.Ref("pi_1"), Given.Eur(60m), clock);
        var cash = Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(30m), orderId: orderId), Given.Eur(30m),
            Given.Ref("TILL-1-0007"), clock);
        card.RegisterRefund(card.RequestRefund(Given.Eur(10m), "Gesture", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(Given.Eur(80m), card.NetPaidAmount + cash.NetPaidAmount);
    }
}

public class PaymentSettledAmountTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, 0)]
    [InlineData(PaymentStatus.Authorized, 0)]
    [InlineData(PaymentStatus.Failed, 0)]
    [InlineData(PaymentStatus.Cancelled, 0)]
    [InlineData(PaymentStatus.Captured, 100)]
    [InlineData(PaymentStatus.PartiallyRefunded, 60)]
    [InlineData(PaymentStatus.Refunded, 0)]
    public void SettledAmount_equals_the_net_paid_amount_when_no_refund_is_in_flight(PaymentStatus status, int expected)
    {
        var payment = Given.PaymentIn(status);

        Assert.Equal(Given.Eur(expected), payment.SettledAmount);
        Assert.Equal(payment.NetPaidAmount, payment.SettledAmount);
    }

    [Fact]
    public void SettledAmount_sets_aside_a_refund_as_soon_as_it_is_requested()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);

        payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);

        // L'argent en cours de remboursement ne peut plus payer la commande, même si le paiement le détient encore.
        Assert.Equal(Given.Eur(60m), payment.SettledAmount);
        Assert.Equal(Given.Eur(100m), payment.NetPaidAmount);
    }

    [Fact]
    public void SettledAmount_keeps_a_succeeded_refund_deducted()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refund = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);

        payment.RegisterRefund(refund, RefundOutcome.Succeeded, clock);

        Assert.Equal(Given.Eur(60m), payment.SettledAmount);
        Assert.Equal(Given.Eur(60m), payment.NetPaidAmount);
    }

    [Fact]
    public void SettledAmount_counts_again_the_amount_of_a_refund_that_failed()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refund = payment.RequestRefund(Given.Eur(40m), "Missing dish", clock);

        payment.RegisterRefund(refund, RefundOutcome.Failed, clock);

        Assert.Equal(Given.Eur(100m), payment.SettledAmount);
        Assert.Equal(Given.Eur(100m), payment.NetPaidAmount);
    }

    [Fact]
    public void SettledAmount_deducts_succeeded_and_pending_refunds_together()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.PartiallyRefunded, clock);
        payment.RequestRefund(Given.Eur(25m), "Second gesture", clock);
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(10m), "Declined by the bank", clock), RefundOutcome.Failed, clock);

        // 100 capturés - 40 remboursés - 25 en cours ; les 10 en échec sont revenus.
        Assert.Equal(Given.Eur(35m), payment.SettledAmount);
        Assert.Equal(Given.Eur(60m), payment.NetPaidAmount);
    }

    [Fact]
    public void SettledAmount_is_zero_while_the_whole_capture_is_being_refunded()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);

        payment.RequestRefund(Given.Eur(100m), "Order cancelled", clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.SettledAmount);
    }

    [Fact]
    public void SettledAmount_of_a_capture_received_after_cancellation_is_always_zero()
    {
        var clock = new FakeClock();
        var payment = Given.CancelledAfterAuthorization(clock);

        payment.RegisterLateCapture(Given.Ref("pi_1"), Given.Eur(100m), clock);
        Assert.Equal(Given.Eur(0m), payment.SettledAmount);

        var refund = payment.RequestRefund(Given.Eur(30m), "Captured after cancellation", clock);
        Assert.Equal(Given.Eur(0m), payment.SettledAmount);

        payment.RegisterRefund(refund, RefundOutcome.Failed, clock);
        Assert.Equal(Given.Eur(0m), payment.SettledAmount);
    }

    [Fact]
    public void Split_bill_settlement_is_the_sum_of_the_settled_amounts_of_the_order_payments()
    {
        var clock = new FakeClock();
        var orderId = OrderId.New();
        var card = Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(90m), orderId: orderId), Given.Eur(60m), clock);
        card.RegisterAttempt(Given.Ref("pi_1"), clock);
        card.Capture(Given.Ref("pi_1"), Given.Eur(60m), clock);
        var cash = Payment.RecordOffline(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(30m), orderId: orderId), Given.Eur(30m),
            Given.Ref("TILL-1-0007"), clock);

        card.RequestRefund(Given.Eur(10m), "Gesture", clock);

        // 90 sont encore détenus, mais seuls 80 peuvent compter pour terminer la commande.
        Assert.Equal(Given.Eur(90m), card.NetPaidAmount + cash.NetPaidAmount);
        Assert.Equal(Given.Eur(80m), card.SettledAmount + cash.SettledAmount);
    }
}

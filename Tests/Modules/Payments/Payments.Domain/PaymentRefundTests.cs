namespace Payments.Domain;

public class PaymentRefundTests
{
    [Fact]
    public void RequestRefund_creates_a_requested_refund_and_reserves_its_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        clock.Advance(TimeSpan.FromHours(1));

        var refundId = payment.RequestRefund(Given.Eur(30m), " Missing dish ", clock);

        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(refundId, refund.Id);
        Assert.Equal(Given.Eur(30m), refund.Amount);
        Assert.Equal("Missing dish", refund.Reason);
        Assert.Equal(RefundStatus.Requested, refund.Status);
        Assert.Equal(clock.UtcNow, refund.RequestedAtUtc);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(30m), payment.PendingRefundAmount);
        Assert.Equal(Given.Eur(70m), payment.RefundableAmount);
        var requested = DomainAssert.Raised<RefundRequested>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, refundId), (requested.TenantId, requested.PaymentId, requested.OrderId, requested.RefundId));
        Assert.Equal(Given.Eur(30m), requested.Amount);
        Assert.Equal("Missing dish", requested.Reason);
        Assert.Equal(clock.UtcNow, requested.OccurredAtUtc);
        DomainAssert.NotRaised<RefundRegistered>(payment);
        DomainAssert.NotRaised<RefundSucceeded>(payment);
    }

    [Fact]
    public void RegisterRefund_success_partially_refunds_the_payment()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(30m), "Missing dish", clock);
        clock.Advance(TimeSpan.FromMinutes(2));

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);

        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(clock.UtcNow, refund.CompletedAtUtc);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(30m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.PendingRefundAmount);
        Assert.Equal(Given.Eur(70m), payment.RefundableAmount);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);

        var registered = DomainAssert.Raised<RefundRegistered>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, refundId), (registered.TenantId, registered.PaymentId, registered.OrderId, registered.RefundId));
        Assert.Equal(Given.Eur(30m), registered.Amount);
        Assert.Equal(RefundOutcome.Succeeded, registered.Outcome);
        Assert.Equal(PaymentStatus.PartiallyRefunded, registered.PaymentStatus);
        var succeeded = DomainAssert.Raised<RefundSucceeded>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, refundId), (succeeded.TenantId, succeeded.PaymentId, succeeded.OrderId, succeeded.RefundId));
        Assert.Equal(Given.Eur(30m), succeeded.Amount);
        Assert.Equal(Given.Eur(30m), succeeded.TotalRefunded);
        Assert.False(succeeded.IsFullRefund);
        Assert.Equal(clock.UtcNow, succeeded.OccurredAtUtc);
    }

    [Fact]
    public void RegisterRefund_success_of_the_whole_captured_amount_refunds_the_payment()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);

        payment.RegisterRefund(payment.RequestRefund(Given.Eur(100m), "Order cancelled", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        Assert.Equal(PaymentStatus.Refunded, DomainAssert.Raised<RefundRegistered>(payment).PaymentStatus);
        Assert.True(DomainAssert.Raised<RefundSucceeded>(payment).IsFullRefund);
    }

    [Fact]
    public void Successive_partial_refunds_end_in_refunded_when_they_reach_the_captured_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);

        payment.RegisterRefund(payment.RequestRefund(Given.Eur(33.33m), "Part 1", clock), RefundOutcome.Succeeded, clock);
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(33.33m), "Part 2", clock), RefundOutcome.Succeeded, clock);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(33.34m), payment.RefundableAmount);
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(33.34m), "Part 3", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        var last = payment.DomainEvents.OfType<RefundSucceeded>().Last();
        Assert.Equal(Given.Eur(100m), last.TotalRefunded);
        Assert.True(last.IsFullRefund);
    }

    [Fact]
    public void Refund_of_a_partial_capture_is_measured_against_the_captured_amount_not_the_requested_one()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);
        payment.Capture(Given.Ref("pi_1"), Given.Eur(80m), clock);

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(80.01m), "Too much", clock));
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(80m), "Everything", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void RegisterRefund_failure_releases_the_reservation_and_leaves_the_status()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(30m), "Missing dish", clock);

        payment.RegisterRefund(refundId, RefundOutcome.Failed, clock);

        Assert.Equal(RefundStatus.Failed, Assert.Single(payment.Refunds).Status);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.PendingRefundAmount);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
        var registered = DomainAssert.Raised<RefundRegistered>(payment);
        Assert.Equal(RefundOutcome.Failed, registered.Outcome);
        Assert.Equal(PaymentStatus.Captured, registered.PaymentStatus);
        DomainAssert.NotRaised<RefundSucceeded>(payment);
    }

    [Fact]
    public void RegisterRefund_failure_on_a_partially_refunded_payment_keeps_it_partially_refunded()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.PartiallyRefunded, clock);

        payment.RegisterRefund(payment.RequestRefund(Given.Eur(60m), "Rest", clock), RefundOutcome.Failed, clock);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(Given.Eur(40m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(60m), payment.RefundableAmount);
    }

    [Fact]
    public void RequestRefund_rejects_more_than_the_captured_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(100.01m), "Too much", clock));

        Assert.Empty(payment.Refunds);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RequestRefund_rejects_more_than_what_is_left_after_succeeded_refunds()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.PartiallyRefunded, clock);

        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(60.01m), "Too much", clock));
        var accepted = payment.RequestRefund(Given.Eur(60m), "Exactly the rest", clock);

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        Assert.NotEqual(default, accepted);
    }

    [Fact]
    public void Two_pending_refunds_cannot_together_exceed_the_captured_amount()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        payment.RequestRefund(Given.Eur(70m), "First request", clock);

        // Seconde tentative sur le même état : le premier remboursement n'est pas encore versé mais déjà réservé.
        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(40m), "Second request", clock));

        Assert.Single(payment.Refunds);
        Assert.Equal(Given.Eur(70m), payment.PendingRefundAmount);
        Assert.Equal(Given.Eur(30m), payment.RefundableAmount);
        payment.RequestRefund(Given.Eur(30m), "Fits exactly", clock);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(0.01m), "One cent too many", clock));
    }

    [Fact]
    public void Failed_refund_frees_its_amount_for_a_new_request()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var first = payment.RequestRefund(Given.Eur(100m), "First request", clock);
        DomainAssert.Throws(PaymentErrors.RefundExceedsRefundable, () => payment.RequestRefund(Given.Eur(1m), "Blocked", clock));

        payment.RegisterRefund(first, RefundOutcome.Failed, clock);
        payment.RegisterRefund(payment.RequestRefund(Given.Eur(100m), "Retry", clock), RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
    }

    [Fact]
    public void Refund_total_never_exceeds_the_captured_amount_over_many_sequences()
    {
        var random = new Random(4242);
        for (var scenario = 0; scenario < 300; scenario++)
        {
            var clock = new FakeClock();
            var payment = Given.PaymentWithOpenAttempt(Given.Xof(10_000m), clock);
            payment.Capture(Given.Ref("pi_1"), Given.Xof(random.Next(1, 10_001)), clock);

            for (var step = 0; step < 12; step++)
            {
                var pending = payment.Refunds.Where(refund => refund.Status == RefundStatus.Requested).ToArray();
                if (pending.Length > 0 && random.Next(2) == 0)
                {
                    var outcome = random.Next(3) == 0 ? RefundOutcome.Failed : RefundOutcome.Succeeded;
                    payment.RegisterRefund(pending[random.Next(pending.Length)].Id, outcome, clock);
                }
                else
                {
                    try
                    {
                        payment.RequestRefund(Given.Xof(random.Next(1, 6_000)), "Random", clock);
                    }
                    catch (DomainException exception)
                    {
                        Assert.Contains(
                            exception.Code,
                            new[] { PaymentErrors.RefundExceedsRefundable.Code, PaymentErrors.RefundNotAllowed.Code });
                    }
                }

                Assert.True(payment.CapturedAmount <= payment.Amount);
                Assert.True(payment.RefundedAmount + payment.PendingRefundAmount <= payment.CapturedAmount);
                Assert.False(payment.RefundableAmount.IsNegative);
                Assert.Equal(payment.RefundedAmount == payment.CapturedAmount, payment.Status == PaymentStatus.Refunded);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequestRefund_rejects_a_zero_or_negative_amount(int amount)
    {
        var payment = Given.CapturedPayment();

        DomainAssert.Throws(PaymentErrors.RefundAmountNotPositive, () => payment.RequestRefund(Given.Eur(amount), "reason", new FakeClock()));

        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void RequestRefund_rejects_another_currency()
    {
        var payment = Given.CapturedPayment();

        DomainAssert.Throws(PaymentErrors.CurrencyMismatch, () => payment.RequestRefund(Given.Xof(10m), "reason", new FakeClock()));

        Assert.Empty(payment.Refunds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void RequestRefund_requires_a_reason(string? reason)
    {
        var payment = Given.CapturedPayment();

        DomainAssert.Throws(PaymentErrors.RefundReasonInvalid, () => payment.RequestRefund(Given.Eur(1m), reason!, new FakeClock()));

        Assert.Empty(payment.Refunds);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    public void RequestRefund_is_rejected_unless_money_is_captured_and_not_fully_refunded(PaymentStatus status)
    {
        var payment = Given.PaymentIn(status);
        var refundCount = payment.Refunds.Count;
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.RefundNotAllowed, () => payment.RequestRefund(Given.Eur(1m), "reason", new FakeClock()));

        Assert.Equal(status, payment.Status);
        Assert.Equal(refundCount, payment.Refunds.Count);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void RegisterRefund_rejects_an_unknown_refund_or_outcome()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(10m), "reason", clock);

        DomainAssert.Throws(PaymentErrors.RefundNotFound, () => payment.RegisterRefund(RefundId.New(), RefundOutcome.Succeeded, clock));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeUnknown, () => payment.RegisterRefund(refundId, (RefundOutcome)9, clock));

        Assert.Equal(RefundStatus.Requested, Assert.Single(payment.Refunds).Status);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void Refunded_payment_rejects_every_way_forward_and_stays_untouched()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Refunded, clock);
        var refundId = Assert.Single(payment.Refunds).Id;
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptNotAllowed, () => payment.RegisterAttempt(Given.Ref("pi_2"), clock));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => payment.Fail(Given.Ref("pi_1"), Given.Code("declined"), clock));
        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(1m), clock));
        DomainAssert.Throws(PaymentErrors.CancelNotAllowed, () => payment.Cancel("Too late", clock));
        DomainAssert.Throws(PaymentErrors.RefundNotAllowed, () => payment.RequestRefund(Given.Eur(0.01m), "One more cent", clock));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => payment.RegisterRefund(refundId, RefundOutcome.Failed, clock));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(100m), payment.RefundedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Two_reserved_refunds_that_both_succeed_end_in_refunded_with_nothing_pending()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var first = payment.RequestRefund(Given.Eur(60m), "First", clock);
        var second = payment.RequestRefund(Given.Eur(40m), "Second", clock);

        payment.RegisterRefund(first, RefundOutcome.Succeeded, clock);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        payment.RegisterRefund(second, RefundOutcome.Succeeded, clock);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.PendingRefundAmount);
    }
}

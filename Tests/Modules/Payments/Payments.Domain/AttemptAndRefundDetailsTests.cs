namespace Payments.Domain;

/// <summary>Étape 4 : code d'échec d'une tentative, référence et date d'issue d'un remboursement.</summary>
public class AttemptAndRefundDetailsTests
{
    [Theory]
    [InlineData("card_declined", "card_declined")]
    [InlineData("  Insufficient_Funds ", "insufficient_funds")]
    [InlineData("3ds.failed", null)]
    [InlineData("card declined", null)]
    [InlineData("", null)]
    public void A_failure_code_is_a_short_lowercase_technical_code(string value, string? expected)
    {
        if (expected is null)
        {
            DomainAssert.Throws(PaymentErrors.FailureCodeInvalid, () => PaymentFailureCode.Create(value));
            return;
        }

        Assert.Equal(expected, PaymentFailureCode.Create(value).Value);
    }

    [Theory]
    [InlineData("Card Declined", "card_declined")]
    [InlineData("INSUFFICIENT_FUNDS", "insufficient_funds")]
    [InlineData("do_not_honor!!", "do_not_honor")]
    [InlineData("  ", "provider_declined")]
    [InlineData(null, "provider_declined")]
    [InlineData("§§§", "provider_declined")]
    [InlineData("05", "provider_declined")]
    public void A_provider_code_is_normalized_and_never_rejected(string? providerCode, string expected) =>
        Assert.Equal(expected, PaymentFailureCode.FromProvider(providerCode).Value);

    [Fact]
    public void A_provider_code_is_bounded()
    {
        var code = PaymentFailureCode.FromProvider(new string('a', 200));

        Assert.Equal(PaymentFailureCode.MaxLength, code.Value.Length);
    }

    [Fact]
    public void Cancelling_closes_the_open_attempt_with_a_dedicated_code()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        payment.Cancel("Client left", clock);

        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentFailureCode.PaymentCancelled, attempt.FailureCode);
        Assert.Null(attempt.FailureReason);
        Assert.False(attempt.IsOpen);
    }

    [Fact]
    public void A_refund_keeps_the_reference_and_the_date_given_by_the_provider()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(30m), "Cold dish", clock);
        clock.Advance(TimeSpan.FromMinutes(2));

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock, Given.Ref("re_1"));

        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(Given.Ref("re_1"), refund.ProviderReference);
        Assert.Equal(clock.UtcNow, refund.CompletedAtUtc);
        Assert.Equal(Given.Ref("re_1"), DomainAssert.Raised<RefundSucceeded>(payment).RefundProviderReference);
        Assert.Equal(Given.Ref("re_1"), DomainAssert.Raised<RefundRegistered>(payment).RefundProviderReference);
    }

    [Fact]
    public void A_replayed_refund_outcome_is_absorbed_but_another_reference_or_outcome_is_refused()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(30m), "Cold dish", clock);
        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock, Given.Ref("re_1"));
        payment.ClearDomainEvents();

        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock, Given.Ref("re_1"));
        payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock);

        DomainAssert.NoEvents(payment);
        Assert.Equal(Given.Eur(30m), payment.RefundedAmount);
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () =>
            payment.RegisterRefund(refundId, RefundOutcome.Succeeded, clock, Given.Ref("re_2")));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () =>
            payment.RegisterRefund(refundId, RefundOutcome.Failed, clock, Given.Ref("re_1")));
    }

    [Fact]
    public void Two_refunds_of_a_payment_cannot_share_a_provider_reference()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var first = payment.RequestRefund(Given.Eur(10m), "Cold dish", clock);
        var second = payment.RequestRefund(Given.Eur(10m), "Missing drink", clock);
        payment.RegisterRefund(first, RefundOutcome.Succeeded, clock, Given.Ref("re_1"));

        DomainAssert.Throws(PaymentErrors.RefundReferenceDuplicate, () =>
            payment.RegisterRefund(second, RefundOutcome.Succeeded, clock, Given.Ref("re_1")));
        Assert.Equal(RefundStatus.Requested, payment.Refunds.Single(refund => refund.Id == second).Status);
    }

    [Fact]
    public void A_failed_refund_keeps_its_reference_and_frees_the_amount_again()
    {
        var clock = new FakeClock();
        var payment = Given.CapturedPayment(clock);
        var refundId = payment.RequestRefund(Given.Eur(100m), "Order cancelled", clock);

        payment.RegisterRefund(refundId, RefundOutcome.Failed, clock, Given.Ref("re_9"));

        var refund = Assert.Single(payment.Refunds);
        Assert.Equal((RefundStatus.Failed, Given.Ref("re_9")), (refund.Status, refund.ProviderReference));
        Assert.NotNull(refund.CompletedAtUtc);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
    }
}

namespace Payments.Domain;

public class PaymentAttemptTests
{
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;

    private static PaymentAttempt NewAttempt() => PaymentAttempt.Create(Given.Ref("pi_1"), Now);

    [Fact]
    public void Create_starts_a_pending_attempt()
    {
        var attempt = NewAttempt();

        Assert.Equal(Given.Ref("pi_1"), attempt.ProviderReference);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(Now, attempt.StartedAtUtc);
        Assert.True(attempt.IsOpen);
        Assert.False(attempt.IsAuthorized);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Null(attempt.FailureCode);
        Assert.Null(attempt.FailureReason);
    }

    [Fact]
    public void Succeed_closes_a_pending_attempt()
    {
        var attempt = NewAttempt();

        attempt.Succeed(Now.AddSeconds(5));

        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(Now.AddSeconds(5), attempt.CompletedAtUtc);
        Assert.False(attempt.IsOpen);
    }

    [Fact]
    public void Fail_closes_a_pending_attempt_with_its_reason()
    {
        var attempt = NewAttempt();

        attempt.Fail(Given.Code("card_declined"), "Your card was declined.", Now.AddSeconds(5));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal("card_declined", attempt.FailureCode!.Value);
        Assert.Equal("Your card was declined.", attempt.FailureReason);
        Assert.Equal(Now.AddSeconds(5), attempt.CompletedAtUtc);
    }

    [Fact]
    public void MarkAuthorized_keeps_the_attempt_pending_and_the_first_authorization_time()
    {
        var attempt = NewAttempt();

        attempt.MarkAuthorized(Now.AddSeconds(1));
        attempt.MarkAuthorized(Now.AddSeconds(9));

        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.True(attempt.IsAuthorized);
        Assert.Equal(Now.AddSeconds(1), attempt.AuthorizedAtUtc);
    }

    [Fact]
    public void MarkCapturedAfterCancellation_flags_a_failed_attempt_without_reopening_it()
    {
        var attempt = NewAttempt();
        attempt.Fail(PaymentFailureCode.PaymentCancelled, null, Now);

        attempt.MarkCapturedAfterCancellation(Now.AddMinutes(4));
        attempt.MarkCapturedAfterCancellation(Now.AddMinutes(9));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.True(attempt.WasCapturedAfterCancellation);
        Assert.Equal(Now.AddMinutes(4), attempt.CapturedAfterCancellationAtUtc);
        Assert.Equal(Now, attempt.CompletedAtUtc);
    }

    [Fact]
    public void MarkCapturedAfterCancellation_is_rejected_on_an_open_or_succeeded_attempt()
    {
        var open = NewAttempt();
        var succeeded = NewAttempt();
        succeeded.Succeed(Now);

        DomainAssert.Throws(PaymentErrors.LateCaptureNotAllowed, () => open.MarkCapturedAfterCancellation(Now));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => succeeded.MarkCapturedAfterCancellation(Now));

        Assert.False(open.WasCapturedAfterCancellation);
        Assert.False(succeeded.WasCapturedAfterCancellation);
    }

    [Fact]
    public void Double_success_is_rejected_because_a_succeeded_attempt_is_final()
    {
        var attempt = NewAttempt();
        attempt.Succeed(Now);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => attempt.Succeed(Now.AddMinutes(1)));

        Assert.Equal(Now, attempt.CompletedAtUtc);
    }

    [Fact]
    public void Succeeded_attempt_cannot_fail_or_be_authorized_again()
    {
        var attempt = NewAttempt();
        attempt.Succeed(Now);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => attempt.Fail(Given.Code("late"), null, Now));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadySucceeded, () => attempt.MarkAuthorized(Now));
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Null(attempt.FailureCode);
    }

    [Fact]
    public void Failed_attempt_cannot_succeed_fail_again_or_be_authorized()
    {
        var attempt = NewAttempt();
        attempt.Fail(Given.Code("card_declined"), null, Now);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => attempt.Succeed(Now));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => attempt.Fail(Given.Code("other"), null, Now));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => attempt.MarkAuthorized(Now));
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal("card_declined", attempt.FailureCode!.Value);
    }
}

public class RefundTests
{
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;

    [Fact]
    public void Request_starts_a_requested_refund()
    {
        var refund = Refund.Request(Given.Eur(12.50m), "  Cold dish ", Now);

        Assert.Equal(Given.Eur(12.50m), refund.Amount);
        Assert.Equal("Cold dish", refund.Reason);
        Assert.Equal(RefundStatus.Requested, refund.Status);
        Assert.Equal(Now, refund.RequestedAtUtc);
        Assert.Null(refund.CompletedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Request_rejects_a_zero_or_negative_amount(int amount) =>
        DomainAssert.Throws(PaymentErrors.RefundAmountNotPositive, () => Refund.Request(Given.Eur(amount), "reason", Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Request_requires_a_reason(string? reason) =>
        DomainAssert.Throws(PaymentErrors.RefundReasonInvalid, () => Refund.Request(Given.Eur(1m), reason, Now));

    [Fact]
    public void Request_rejects_a_too_long_reason() =>
        DomainAssert.Throws(
            PaymentErrors.RefundReasonInvalid,
            () => Refund.Request(Given.Eur(1m), new string('r', Refund.MaxReasonLength + 1), Now));

    [Fact]
    public void Succeed_and_Fail_close_a_requested_refund()
    {
        var succeeded = Refund.Request(Given.Eur(1m), "reason", Now);
        var failed = Refund.Request(Given.Eur(1m), "reason", Now);

        succeeded.Succeed(Now.AddMinutes(1));
        failed.Fail(Now.AddMinutes(2));

        Assert.Equal(RefundStatus.Succeeded, succeeded.Status);
        Assert.Equal(Now.AddMinutes(1), succeeded.CompletedAtUtc);
        Assert.Equal(RefundStatus.Failed, failed.Status);
        Assert.Equal(Now.AddMinutes(2), failed.CompletedAtUtc);
    }

    [Fact]
    public void Closed_refund_is_final()
    {
        var succeeded = Refund.Request(Given.Eur(1m), "reason", Now);
        succeeded.Succeed(Now);
        var failed = Refund.Request(Given.Eur(1m), "reason", Now);
        failed.Fail(Now);

        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => succeeded.Succeed(Now));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => succeeded.Fail(Now));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => failed.Succeed(Now));
        DomainAssert.Throws(PaymentErrors.RefundOutcomeConflict, () => failed.Fail(Now));
        Assert.Equal(RefundStatus.Succeeded, succeeded.Status);
        Assert.Equal(RefundStatus.Failed, failed.Status);
    }
}

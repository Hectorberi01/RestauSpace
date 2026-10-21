namespace Payments.Domain;

public class PaymentFlowTests
{
    [Fact]
    public void Create_starts_a_pending_payment_with_nothing_captured()
    {
        var clock = new FakeClock();
        var order = Given.Order(Given.Eur(42.50m));
        var orderId = order.OrderId;

        var payment = Payment.Create(Given.Online, PaymentMethodType.Card, order, Given.Eur(42.50m), clock);

        Assert.Equal(Given.Tenant, payment.TenantId);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(Given.Eur(42.50m), payment.Amount);
        Assert.Equal(Currency.EUR, payment.Currency);
        Assert.Equal(Given.Stripe, payment.Provider);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundedAmount);
        Assert.Equal(Given.Eur(0m), payment.PendingRefundAmount);
        Assert.Equal(Given.Eur(0m), payment.RefundableAmount);
        Assert.Equal(Given.Eur(0m), payment.NetPaidAmount);
        Assert.False(payment.HasUnexpectedCapture);
        Assert.Empty(payment.Attempts);
        Assert.Empty(payment.Refunds);
        Assert.Equal(clock.UtcNow, payment.CreatedAtUtc);
        Assert.Null(payment.CapturedAtUtc);
    }

    [Fact]
    public void Create_raises_PaymentCreated()
    {
        var clock = new FakeClock();

        var payment = Given.NewPayment(Given.Xof(5000m), clock);

        var created = DomainAssert.Raised<PaymentCreated>(payment);
        Assert.Equal(Given.Tenant, created.TenantId);
        Assert.Equal(payment.Id, created.PaymentId);
        Assert.Equal(payment.OrderId, created.OrderId);
        Assert.Equal(Given.Xof(5000m), created.Amount);
        Assert.Equal(Given.Stripe, created.Provider);
        Assert.Equal(clock.UtcNow, created.OccurredAtUtc);
        Assert.Single(payment.DomainEvents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_rejects_a_zero_or_negative_amount(int amount) =>
        DomainAssert.Throws(
            PaymentErrors.AmountNotPositive,
            () => Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(100m)), Given.Eur(amount), new FakeClock()));

    [Fact]
    public void Create_requires_configuration_order_amount_and_clock()
    {
        var order = Given.Order(Given.Eur(1m));
        Assert.Throws<ArgumentNullException>(() => Payment.Create(Given.Online, PaymentMethodType.Card, null!, Given.Eur(1m), new FakeClock()));
        Assert.Throws<ArgumentNullException>(() => Payment.Create(Given.Online, PaymentMethodType.Card, order, null!, new FakeClock()));
        Assert.Throws<ArgumentNullException>(() => Payment.Create(null!, PaymentMethodType.Card, order, Given.Eur(1m), new FakeClock()));
        Assert.Throws<ArgumentNullException>(() => Payment.Create(Given.Online, PaymentMethodType.Card, order, Given.Eur(1m), null!));
    }

    [Fact]
    public void Create_rejects_an_order_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(100m), TenantId.New()), Given.Eur(100m), new FakeClock()));

    [Fact]
    public void Create_rejects_an_amount_in_another_currency_than_the_order() =>
        DomainAssert.Throws(
            PaymentErrors.CurrencyMismatch,
            () => Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(100m)), Given.Xof(50m), new FakeClock()));

    [Fact]
    public void Create_rejects_more_than_the_amount_due_and_accepts_exactly_it()
    {
        var order = Given.Order(Given.Eur(42.50m));

        DomainAssert.Throws(
            PaymentErrors.AmountExceedsAmountDue,
            () => Payment.Create(Given.Online, PaymentMethodType.Card, order, Given.Eur(42.51m), new FakeClock()));

        Assert.Equal(Given.Eur(42.50m), Payment.Create(Given.Online, PaymentMethodType.Card, order, Given.Eur(42.50m), new FakeClock()).Amount);
    }

    [Fact]
    public void Create_accepts_several_partial_payments_for_the_same_order()
    {
        var orderId = OrderId.New();
        var clock = new FakeClock();

        // Addition partagée : l'Application réduit le montant dû à mesure que les autres paiements couvrent la commande.
        var first = Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(90m), orderId: orderId), Given.Eur(30m), clock);
        var second = Payment.Create(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(60m), orderId: orderId), Given.Eur(60m), clock);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(orderId, first.OrderId);
        Assert.Equal(orderId, second.OrderId);
        Assert.Equal(Given.Eur(30m), first.Amount);
        Assert.Equal(Given.Eur(60m), second.Amount);
    }

    [Fact]
    public void RegisterAttempt_opens_a_pending_attempt()
    {
        var clock = new FakeClock();
        var payment = Given.NewPayment(clock: clock);
        clock.Advance(TimeSpan.FromSeconds(30));

        var attemptId = payment.RegisterAttempt(Given.Ref("pi_1"), clock);

        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(attemptId, attempt.Id);
        Assert.Equal(Given.Ref("pi_1"), attempt.ProviderReference);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(clock.UtcNow, attempt.StartedAtUtc);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        var registered = DomainAssert.Raised<PaymentAttemptRegistered>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, attemptId), (registered.TenantId, registered.PaymentId, registered.OrderId, registered.AttemptId));
        Assert.Equal(Given.Ref("pi_1"), registered.ProviderReference);
        Assert.Equal(clock.UtcNow, registered.OccurredAtUtc);
    }

    [Fact]
    public void RegisterAttempt_rejects_a_second_attempt_while_one_is_open()
    {
        var payment = Given.PaymentWithOpenAttempt();

        DomainAssert.Throws(PaymentErrors.AttemptInProgress, () => payment.RegisterAttempt(Given.Ref("pi_2"), new FakeClock()));

        Assert.Single(payment.Attempts);
    }

    [Fact]
    public void RegisterAttempt_rejects_a_second_attempt_while_the_payment_is_authorized()
    {
        var payment = Given.PaymentIn(PaymentStatus.Authorized);

        DomainAssert.Throws(PaymentErrors.AttemptInProgress, () => payment.RegisterAttempt(Given.Ref("pi_2"), new FakeClock()));

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public void RegisterAttempt_rejects_a_provider_reference_already_used_by_this_payment(PaymentStatus status)
    {
        var payment = Given.PaymentIn(status);

        DomainAssert.Throws(PaymentErrors.AttemptReferenceDuplicate, () => payment.RegisterAttempt(Given.Ref("pi_1"), new FakeClock()));

        Assert.Single(payment.Attempts);
        Assert.Equal(status, payment.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public void RegisterAttempt_rejects_a_payment_that_was_already_captured(PaymentStatus status)
    {
        var payment = Given.PaymentIn(status);

        DomainAssert.Throws(PaymentErrors.AttemptNotAllowed, () => payment.RegisterAttempt(Given.Ref("pi_2"), new FakeClock()));

        Assert.Single(payment.Attempts);
        Assert.Equal(status, payment.Status);
    }

    [Fact]
    public void Authorize_moves_a_pending_payment_to_authorized_without_capturing()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);
        clock.Advance(TimeSpan.FromSeconds(2));

        payment.Authorize(Given.Ref("pi_1"), clock);

        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(clock.UtcNow, attempt.AuthorizedAtUtc);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        var authorized = DomainAssert.Raised<PaymentAuthorized>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, attempt.Id), (authorized.TenantId, authorized.PaymentId, authorized.OrderId, authorized.AttemptId));
        Assert.Equal(Given.Ref("pi_1"), authorized.ProviderReference);
        Assert.Equal(Given.Eur(100m), authorized.Amount);
        Assert.Equal(clock.UtcNow, authorized.OccurredAtUtc);
    }

    [Fact]
    public void Capture_after_authorization_captures_the_payment()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Authorized, clock);
        clock.Advance(TimeSpan.FromMinutes(1));

        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);

        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(100m), payment.RefundableAmount);
        Assert.Equal(clock.UtcNow, payment.CapturedAtUtc);
        var captured = DomainAssert.Raised<PaymentCaptured>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, attempt.Id), (captured.TenantId, captured.PaymentId, captured.OrderId, captured.AttemptId));
        Assert.Equal(Given.Ref("pi_1"), captured.ProviderReference);
        Assert.Equal(Given.Eur(100m), captured.CapturedAmount);
        Assert.Equal(clock.UtcNow, captured.OccurredAtUtc);
    }

    [Fact]
    public void Capture_directly_from_pending_is_allowed()
    {
        var payment = Given.PaymentWithOpenAttempt();

        payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), new FakeClock());

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        DomainAssert.NotRaised<PaymentAuthorized>(payment);
        DomainAssert.Raised<PaymentCaptured>(payment);
    }

    [Fact]
    public void Capture_of_less_than_the_amount_is_recorded_as_captured_for_that_lower_amount()
    {
        var payment = Given.PaymentWithOpenAttempt();

        payment.Capture(Given.Ref("pi_1"), Given.Eur(80m), new FakeClock());

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.Amount);
        Assert.Equal(Given.Eur(80m), payment.CapturedAmount);
        Assert.Equal(Given.Eur(80m), payment.RefundableAmount);
    }

    [Fact]
    public void Capture_rejects_more_than_the_payment_amount()
    {
        var payment = Given.PaymentWithOpenAttempt();
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.CaptureExceedsAmount, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100.01m), new FakeClock()));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        Assert.Equal(PaymentAttemptStatus.Pending, Assert.Single(payment.Attempts).Status);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capture_rejects_a_zero_or_negative_amount(int amount)
    {
        var payment = Given.PaymentWithOpenAttempt();

        DomainAssert.Throws(PaymentErrors.CaptureAmountNotPositive, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(amount), new FakeClock()));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void Capture_rejects_another_currency()
    {
        var payment = Given.PaymentWithOpenAttempt();

        DomainAssert.Throws(PaymentErrors.CurrencyMismatch, () => payment.Capture(Given.Ref("pi_1"), Given.Xof(100m), new FakeClock()));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
    }

    [Fact]
    public void Provider_results_for_an_unknown_reference_are_rejected()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => payment.Authorize(Given.Ref("pi_unknown"), clock));
        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => payment.Capture(Given.Ref("pi_unknown"), Given.Eur(100m), clock));
        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => payment.Fail(Given.Ref("pi_unknown"), Given.Code("declined"), clock));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Provider_results_cannot_be_applied_before_any_attempt_is_registered()
    {
        var payment = Given.NewPayment();

        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), new FakeClock()));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    public void Fail_moves_the_payment_to_failed_with_its_code_and_reason(PaymentStatus status)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(status, clock);
        clock.Advance(TimeSpan.FromSeconds(10));

        payment.Fail(Given.Ref("pi_1"), PaymentFailureCode.Create("  Card_Declined "), clock, "  Your card was declined. ");

        var attempt = Assert.Single(payment.Attempts);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(Given.Code("card_declined"), attempt.FailureCode);
        Assert.Equal("Your card was declined.", attempt.FailureReason);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        var failed = DomainAssert.Raised<PaymentFailed>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId, attempt.Id), (failed.TenantId, failed.PaymentId, failed.OrderId, failed.AttemptId));
        Assert.Equal((Given.Code("card_declined"), "Your card was declined."), (failed.FailureCode, failed.Reason));
        Assert.Equal(clock.UtcNow, failed.OccurredAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Fail_needs_a_code_but_no_reason(string? reason)
    {
        var payment = Given.PaymentWithOpenAttempt();

        payment.Fail(Given.Ref("pi_1"), Given.Code("expired"), new FakeClock(), reason);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(Assert.Single(payment.Attempts).FailureReason);
        Assert.Throws<ArgumentNullException>(() => Given.PaymentWithOpenAttempt().Fail(Given.Ref("pi_1"), null!, new FakeClock()));
    }

    [Fact]
    public void Fail_rejects_a_too_long_reason() =>
        DomainAssert.Throws(
            PaymentErrors.FailureReasonInvalid,
            () => Given.PaymentWithOpenAttempt().Fail(
                Given.Ref("pi_1"), Given.Code("declined"), new FakeClock(), new string('r', PaymentAttempt.MaxFailureReasonLength + 1)));

    [Fact]
    public void Failed_payment_cannot_be_captured_through_its_failed_attempt()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Authorize(Given.Ref("pi_1"), clock));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Failed_payment_cannot_be_captured_through_a_reference_that_was_never_registered()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);

        DomainAssert.Throws(PaymentErrors.AttemptNotFound, () => payment.Capture(Given.Ref("pi_2"), Given.Eur(100m), clock));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void Failed_payment_is_captured_only_through_a_new_registered_attempt_that_succeeds()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);

        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        payment.Capture(Given.Ref("pi_2"), Given.Eur(100m), clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.Equal(
            [PaymentAttemptStatus.Failed, PaymentAttemptStatus.Succeeded],
            payment.Attempts.Select(attempt => attempt.Status).ToArray());
        DomainAssert.Raised<PaymentFailed>(payment);
        DomainAssert.Raised<PaymentCaptured>(payment);
    }

    [Fact]
    public void Retry_that_fails_again_leaves_the_payment_failed_and_can_be_retried_once_more()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);

        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        payment.Fail(Given.Ref("pi_2"), Given.Code("insufficient_funds"), clock);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        payment.RegisterAttempt(Given.Ref("pi_3"), clock);
        payment.Authorize(Given.Ref("pi_3"), clock);
        payment.Capture(Given.Ref("pi_3"), Given.Eur(100m), clock);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(3, payment.Attempts.Count);
        Assert.Single(payment.Attempts, attempt => attempt.Status == PaymentAttemptStatus.Succeeded);
    }

    [Fact]
    public void Old_failed_attempt_cannot_capture_once_a_retry_is_open()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.RegisterAttempt(Given.Ref("pi_2"), clock);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed)]
    public void Cancel_is_allowed_before_capture(PaymentStatus status)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(status, clock);
        clock.Advance(TimeSpan.FromMinutes(5));

        payment.Cancel("  Order cancelled ", clock);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("Order cancelled", payment.CancellationReason);
        Assert.Equal(clock.UtcNow, payment.CancelledAtUtc);
        Assert.DoesNotContain(payment.Attempts, attempt => attempt.IsOpen);
        var cancelled = DomainAssert.Raised<PaymentCancelled>(payment);
        Assert.Equal((Given.Tenant, payment.Id, payment.OrderId), (cancelled.TenantId, cancelled.PaymentId, cancelled.OrderId));
        Assert.Equal("Order cancelled", cancelled.Reason);
        Assert.Equal(clock.UtcNow, cancelled.OccurredAtUtc);
        // Pending et Authorized avaient une tentative ouverte à annuler chez le prestataire ; Failed n'en avait plus.
        var attempt = Assert.Single(payment.Attempts);
        if (status == PaymentStatus.Failed)
        {
            Assert.Null(cancelled.ClosedAttemptId);
            Assert.Null(cancelled.ClosedAttemptProviderReference);
        }
        else
        {
            Assert.Equal(attempt.Id, cancelled.ClosedAttemptId);
            Assert.Equal(Given.Ref("pi_1"), cancelled.ClosedAttemptProviderReference);
        }
    }

    [Fact]
    public void Cancel_names_the_open_retry_not_the_attempt_that_failed_before_it()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        var retry = payment.RegisterAttempt(Given.Ref("pi_2"), clock);

        payment.Cancel("Order cancelled", clock);

        var cancelled = DomainAssert.Raised<PaymentCancelled>(payment);
        Assert.Equal(retry, cancelled.ClosedAttemptId);
        Assert.Equal(Given.Ref("pi_2"), cancelled.ClosedAttemptProviderReference);
    }

    [Fact]
    public void Cancel_is_allowed_before_any_attempt()
    {
        var payment = Given.NewPayment();

        payment.Cancel("Customer left", new FakeClock());

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Empty(payment.Attempts);
        var cancelled = DomainAssert.Raised<PaymentCancelled>(payment);
        Assert.Null(cancelled.ClosedAttemptId);
        Assert.Null(cancelled.ClosedAttemptProviderReference);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public void Cancel_is_rejected_once_the_payment_is_captured(PaymentStatus status)
    {
        var payment = Given.PaymentIn(status);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.CancelNotAllowed, () => payment.Cancel("Too late", new FakeClock()));

        Assert.Equal(status, payment.Status);
        Assert.Null(payment.CancelledAtUtc);
        DomainAssert.NoEvents(payment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Cancel_requires_a_reason(string? reason)
    {
        var payment = Given.PaymentWithOpenAttempt();

        DomainAssert.Throws(PaymentErrors.CancellationReasonInvalid, () => payment.Cancel(reason!, new FakeClock()));
        DomainAssert.Throws(
            PaymentErrors.CancellationReasonInvalid,
            () => payment.Cancel(new string('r', Payment.MaxCancellationReasonLength + 1), new FakeClock()));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.True(Assert.Single(payment.Attempts).IsOpen);
    }

    [Fact]
    public void Cancelled_payment_rejects_every_way_forward_and_stays_untouched()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Cancelled, clock);
        payment.ClearDomainEvents();

        DomainAssert.Throws(PaymentErrors.AlreadyCancelled, () => payment.RegisterAttempt(Given.Ref("pi_2"), clock));
        DomainAssert.Throws(PaymentErrors.AlreadyCancelled, () => payment.Cancel("Again", clock));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Authorize(Given.Ref("pi_1"), clock));
        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));
        DomainAssert.Throws(PaymentErrors.RefundNotAllowed, () => payment.RequestRefund(Given.Eur(1m), "reason", clock));
        DomainAssert.Throws(PaymentErrors.RefundNotFound, () => payment.RegisterRefund(RefundId.New(), RefundOutcome.Succeeded, clock));

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        Assert.Equal("Order cancelled", payment.CancellationReason);
        Assert.Single(payment.Attempts);
        DomainAssert.NoEvents(payment);
    }

    [Fact]
    public void Late_capture_for_an_attempt_closed_by_the_cancellation_is_rejected_loudly()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Authorized, clock);
        payment.Cancel("Order cancelled", clock);

        DomainAssert.Throws(PaymentErrors.AttemptAlreadyFailed, () => payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock));

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(Given.Eur(0m), payment.CapturedAmount);
        Assert.False(payment.HasUnexpectedCapture);
        DomainAssert.NotRaised<PaymentCaptured>(payment);
        DomainAssert.NotRaised<PaymentCapturedAfterCancellation>(payment);
    }

    [Fact]
    public void Captured_amount_never_exceeds_the_amount_whatever_the_path()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(PaymentStatus.Failed, clock);
        payment.RegisterAttempt(Given.Ref("pi_2"), clock);
        payment.Capture(Given.Ref("pi_2"), Given.Eur(100m), clock);

        // Aucune seconde capture possible : pas de nouvelle tentative, et celle réussie n'accepte que son rejeu exact.
        DomainAssert.Throws(PaymentErrors.AttemptNotAllowed, () => payment.RegisterAttempt(Given.Ref("pi_3"), clock));
        DomainAssert.Throws(PaymentErrors.CaptureConflict, () => payment.Capture(Given.Ref("pi_2"), Given.Eur(50m), clock));

        Assert.Equal(Given.Eur(100m), payment.CapturedAmount);
        Assert.True(payment.CapturedAmount <= payment.Amount);
    }

    [Fact]
    public void Payment_and_its_children_expose_no_public_setter()
    {
        var setters = new[] { typeof(Payment), typeof(PaymentAttempt), typeof(Refund) }
            .SelectMany(type => type.GetProperties())
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}");

        Assert.Empty(setters);
    }
}

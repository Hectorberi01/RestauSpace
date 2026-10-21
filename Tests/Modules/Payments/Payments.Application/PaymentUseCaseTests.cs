using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Application.Queries;
using Payments.Domain;

namespace Payments.Application;

public sealed class PaymentUseCaseTests
{
    private readonly PaymentsFixture _fixture = new();

    private Task<Guid> CashAsync(Guid order, decimal? amount = null, string reference = "T-12") =>
        _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, "cash", reference, amount));

    private Task<OnlinePaymentStarted> OnlineAsync(Guid order, decimal? amount = null) =>
        _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(order, "fakepay", amount, PaymentMethodType.Card));

    private Task ResultAsync(ProviderResultKind kind, string reference, decimal? amount = null, Guid? refundId = null, string? reason = null) =>
        _fixture.Apply.HandleAsync(new ApplyProviderResultCommand("fakepay", kind, reference, amount, amount is null ? null : "XOF", refundId, reason));

    [Fact]
    public async Task Cash_received_at_the_counter_settles_the_whole_order_by_default()
    {
        var order = _fixture.GivenOrder();

        var id = await CashAsync(order);

        var payment = _fixture.Stored(id);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(Money.Of(7000m, Currency.XOF), payment.CapturedAmount);
        Assert.Equal("T-12", Assert.Single(payment.Attempts).ProviderReference.Value);
        Assert.Equal(new[] { "lock", "list", "save" }, _fixture.Calls.ToArray());
        Assert.Equal(7000m, await _fixture.GetSettled.HandleAsync(new GetOrderSettledAmountQuery(order, "XOF")));
        DomainAssert.Raised<PaymentCaptured>(payment);
    }

    [Fact]
    public async Task An_online_sales_app_return_page_reaches_the_provider_and_staff_payments_keep_the_platform_one()
    {
        var order = _fixture.GivenOrder();
        var back = new Uri("https://chezawa.bj/commande?jeton=abc");

        await _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(order, "fakepay", 3000m, PaymentMethodType.Card, ReturnUrl: back));
        await OnlineAsync(order, 1000m);

        Assert.Equal([back, null], _fixture.Gateway.Started.Select(request => request.ReturnUrl));
    }

    [Fact]
    public async Task A_shared_bill_is_paid_in_parts_and_never_beyond_its_total()
    {
        var order = _fixture.GivenOrder();
        await CashAsync(order, 2500m);
        await _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, "mobile_money", "MOMO-881", 3000m));

        var tooMuch = await Assert.ThrowsAsync<DomainException>(() => CashAsync(order, 1501m));
        await CashAsync(order);
        var nothingLeft = await Assert.ThrowsAsync<DomainException>(() => CashAsync(order));

        var view = await _fixture.GetOrderPayments.HandleAsync(new GetOrderPaymentsQuery(order));
        Assert.Equal(PaymentErrors.AmountExceedsAmountDue, tooMuch.Error);
        Assert.Equal(PaymentsApplicationErrors.NothingDue, nothingLeft.Error);
        Assert.Equal(new[] { 2500m, 3000m, 1500m }, view.Payments.Select(payment => payment.Amount).ToArray());
        Assert.Equal((7000m, 7000m, 0m), (view.Total, view.SettledAmount, view.AmountDue));
        Assert.All(view.Payments, payment => Assert.True(payment.IsOffline));
    }

    [Fact]
    public async Task An_online_payment_reserves_its_amount_until_it_is_cancelled()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order, 3000m);

        var reserved = await _fixture.GetOrderPayments.HandleAsync(new GetOrderPaymentsQuery(order));
        await ResultAsync(ProviderResultKind.Failed, started.ProviderReference, reason: "Insufficient funds");
        var afterFailure = await _fixture.GetOrderPayments.HandleAsync(new GetOrderPaymentsQuery(order));
        await _fixture.Cancel.HandleAsync(new CancelPaymentCommand(started.PaymentId, "Client paid cash"));
        var released = await _fixture.GetOrderPayments.HandleAsync(new GetOrderPaymentsQuery(order));

        Assert.Equal("fp_1", started.ProviderReference);
        Assert.Equal(new Uri("https://pay.example/fp_1"), started.CheckoutUrl);
        Assert.Equal(Money.Of(3000m, Currency.XOF), Assert.Single(_fixture.Gateway.Started).Amount);
        Assert.Equal(PaymentStatus.Pending, reserved.Payments.Single().Status);
        Assert.Equal((0m, 4000m), (reserved.SettledAmount, reserved.AmountDue));
        Assert.Equal(4000m, afterFailure.AmountDue);
        Assert.Equal("Insufficient funds", afterFailure.Payments.Single().Attempts.Single().FailureReason);
        Assert.Equal("insufficient_funds", afterFailure.Payments.Single().Attempts.Single().FailureCode);
        Assert.Equal(7000m, released.AmountDue);
        Assert.Equal(PaymentStatus.Cancelled, _fixture.Stored(started.PaymentId).Status);
    }

    [Fact]
    public async Task The_provider_confirms_an_online_payment_once_and_a_contradiction_is_refused()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order);

        await ResultAsync(ProviderResultKind.Authorized, started.ProviderReference);
        await ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 7000m);
        await ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 7000m);
        var conflict = await Assert.ThrowsAsync<DomainException>(() => ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 6000m));
        var incomplete = await Assert.ThrowsAsync<DomainException>(() => ResultAsync(ProviderResultKind.Captured, started.ProviderReference));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => ResultAsync(ProviderResultKind.Captured, "fp_404", 7000m));

        var payment = _fixture.Stored(started.PaymentId);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Single(payment.DomainEvents.OfType<PaymentCaptured>());
        Assert.Equal(PaymentErrors.CaptureConflict, conflict.Error);
        Assert.Equal(PaymentsApplicationErrors.ProviderResultInvalid, incomplete.Error);
        Assert.Equal(PaymentsApplicationErrors.PaymentNotFound, unknown.Error);
        Assert.Equal(7000m, await _fixture.GetSettled.HandleAsync(new GetOrderSettledAmountQuery(order, "XOF")));
    }

    [Fact]
    public async Task Money_taken_after_a_cancellation_is_recorded_to_be_refunded_and_settles_nothing()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order);
        await _fixture.Cancel.HandleAsync(new CancelPaymentCommand(started.PaymentId, "Too slow"));

        await ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 7000m);
        var refund = await _fixture.Refund.HandleAsync(new RequestRefundCommand(started.PaymentId, "Captured after cancellation"));

        var payment = await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(started.PaymentId));
        Assert.True(payment.HasUnexpectedCapture);
        Assert.Equal(0m, payment.SettledAmount);
        Assert.Equal(7000m, payment.PendingRefundAmount);
        Assert.Equal(refund, Assert.Single(payment.Refunds).Id);
        Assert.Equal(0m, await _fixture.GetSettled.HandleAsync(new GetOrderSettledAmountQuery(order, "XOF")));
        DomainAssert.Raised<PaymentCapturedAfterCancellation>(_fixture.Stored(started.PaymentId));
    }

    [Fact]
    public async Task Cash_is_refunded_at_once_and_an_online_refund_waits_for_the_provider()
    {
        var cashOrder = _fixture.GivenOrder();
        var cash = await CashAsync(cashOrder);
        var onlineOrder = _fixture.GivenOrder();
        var online = await OnlineAsync(onlineOrder);
        await ResultAsync(ProviderResultKind.Captured, online.ProviderReference, 7000m);

        await _fixture.Refund.HandleAsync(new RequestRefundCommand(cash, "Cold dish", 2000m));
        var refundId = await _fixture.Refund.HandleAsync(new RequestRefundCommand(online.PaymentId, "Order cancelled"));
        var pending = await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(online.PaymentId));
        await ResultAsync(ProviderResultKind.RefundSucceeded, online.ProviderReference, refundId: refundId);

        var refundedCash = _fixture.Stored(cash);
        Assert.Equal(PaymentStatus.PartiallyRefunded, refundedCash.Status);
        Assert.Equal(5000m, await _fixture.GetSettled.HandleAsync(new GetOrderSettledAmountQuery(cashOrder, "XOF")));
        Assert.Equal((RefundStatus.Requested, 0m), (pending.Refunds.Single().Status, pending.SettledAmount));
        Assert.Equal(PaymentStatus.Refunded, _fixture.Stored(online.PaymentId).Status);
        var noId = await Assert.ThrowsAsync<DomainException>(() => ResultAsync(ProviderResultKind.RefundFailed, online.ProviderReference));
        Assert.Equal(PaymentsApplicationErrors.ProviderResultInvalid, noId.Error);
    }

    [Fact]
    public async Task A_failed_online_payment_is_retried_with_a_new_intent()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order);
        var tooEarly = await Assert.ThrowsAsync<DomainException>(() => _fixture.Retry.HandleAsync(new RetryPaymentCommand(started.PaymentId)));
        await ResultAsync(ProviderResultKind.Failed, started.ProviderReference);

        var retry = await _fixture.Retry.HandleAsync(new RetryPaymentCommand(started.PaymentId));
        await ResultAsync(ProviderResultKind.Captured, retry.ProviderReference, 7000m);

        var payment = _fixture.Stored(started.PaymentId);
        Assert.Equal(PaymentsApplicationErrors.RetryNotAllowed, tooEarly.Error);
        Assert.Equal("fp_2", retry.ProviderReference);
        Assert.Equal(new[] { PaymentAttemptStatus.Failed, PaymentAttemptStatus.Succeeded }, payment.Attempts.Select(attempt => attempt.Status).ToArray());
        // Refus sans motif : un code générique, aucun message.
        Assert.Equal(PaymentFailureCode.ProviderDeclined, payment.Attempts.First().FailureCode);
        Assert.Null(payment.Attempts.First().FailureReason);
        var cash = await CashAsync(_fixture.GivenOrder());
        var offline = await Assert.ThrowsAsync<DomainException>(() => _fixture.Retry.HandleAsync(new RetryPaymentCommand(cash)));
        Assert.Equal(PaymentsApplicationErrors.RetryNotAllowed, offline.Error);
    }

    [Fact]
    public async Task Methods_and_orders_are_checked_before_anything_is_recorded()
    {
        var order = _fixture.GivenOrder();
        var draft = _fixture.GivenOrder(acceptsPayments: false);

        var onlineAtCounter = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, "fakepay", "X-1")));
        var notInCatalog = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(order, "stripe")));
        var cashOnline = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(order, "cash")));
        var notAccepted = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, "card_terminal", "TPE-1")));
        var notPayable = await Assert.ThrowsAsync<DomainException>(() => CashAsync(draft));
        var unknownOrder = await Assert.ThrowsAsync<NotFoundException>(() => CashAsync(Guid.NewGuid()));
        var noReference = await Assert.ThrowsAsync<DomainException>(() => CashAsync(order, reference: " "));
        _fixture.Gateway.Down = true;
        var down = await Assert.ThrowsAsync<DomainException>(() => OnlineAsync(order));

        Assert.Equal(PaymentsApplicationErrors.MethodTypeMismatch, onlineAtCounter.Error);
        Assert.Equal(PaymentsApplicationErrors.MethodNotAccepted, notInCatalog.Error);
        Assert.Equal(PaymentsApplicationErrors.MethodTypeMismatch, cashOnline.Error);
        Assert.Equal(PaymentsApplicationErrors.MethodNotAccepted, notAccepted.Error);
        Assert.Equal(PaymentsApplicationErrors.OrderNotPayable, notPayable.Error);
        Assert.Equal(PaymentsApplicationErrors.OrderNotFound, unknownOrder.Error);
        Assert.Equal(PaymentErrors.ProviderReferenceInvalid, noReference.Error);
        Assert.Equal(PaymentsApplicationErrors.GatewayFailed, down.Error);
        Assert.IsType<PaymentGatewayException>(down.InnerException);
        var released = Assert.Single(_fixture.Payments);
        Assert.Equal(PaymentStatus.Cancelled, released.Status);
        Assert.Equal(StartOnlinePaymentHandler.GatewayUnavailableReason, released.CancellationReason);
        Assert.Empty(released.Attempts);
        Assert.Equal(2, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_method_withdrawn_by_the_platform_or_disabled_by_the_restaurant_takes_no_new_payment()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order, 3000m);
        _fixture.Provider("fakepay").Disable(_fixture.Clock);
        _fixture.Configuration(_fixture.TenantA, "cash").Disable(_fixture.Clock);

        var withdrawn = await Assert.ThrowsAsync<DomainException>(() => OnlineAsync(order));
        var disabled = await Assert.ThrowsAsync<DomainException>(() => CashAsync(order));
        // Ce qui est déjà en cours reste suivi : le prestataire confirme, l'argent est enregistré.
        await ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 3000m);

        Assert.Equal(PaymentsApplicationErrors.MethodNotAccepted, withdrawn.Error);
        Assert.Equal(PaymentsApplicationErrors.MethodNotAccepted, disabled.Error);
        Assert.Equal(PaymentStatus.Captured, _fixture.Stored(started.PaymentId).Status);
    }

    [Fact]
    public async Task An_online_payment_uses_the_merchant_account_of_its_restaurant()
    {
        await OnlineAsync(_fixture.GivenOrder());

        Assert.Equal("sk_a", Assert.Single(_fixture.GatewayCredentials)["api_key"]);
    }

    [Fact]
    public async Task A_provider_limited_to_some_currencies_takes_no_payment_in_another()
    {
        _fixture.Provider("cash").ChangeCapabilities(
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Cash }, currencies: new[] { Currency.EUR }), _fixture.Clock);

        var refused = await Assert.ThrowsAsync<DomainException>(() => CashAsync(_fixture.GivenOrder()));

        Assert.Equal(PaymentsApplicationErrors.MethodNotAccepted, refused.Error);
    }

    [Fact]
    public async Task A_suspended_restaurant_takes_no_payment_but_what_the_provider_did_is_still_recorded()
    {
        var order = _fixture.GivenOrder();
        var started = await OnlineAsync(order);
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => CashAsync(_fixture.GivenOrder()));
        await ResultAsync(ProviderResultKind.Captured, started.ProviderReference, 7000m);

        Assert.Equal(PaymentsApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(PaymentStatus.Captured, (await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(started.PaymentId))).Status);
    }

    [Fact]
    public async Task The_payments_of_another_restaurant_are_invisible()
    {
        var order = _fixture.GivenOrder();
        var id = await CashAsync(order);
        _fixture.CurrentTenantId = _fixture.TenantB;

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(id)));
        var refund = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Refund.HandleAsync(new RequestRefundCommand(id, "Theft")));
        var pay = await Assert.ThrowsAsync<NotFoundException>(() => CashAsync(order));

        Assert.Equal(PaymentsApplicationErrors.PaymentNotFound, read.Error);
        Assert.Equal(PaymentsApplicationErrors.PaymentNotFound, refund.Error);
        Assert.Equal(PaymentsApplicationErrors.OrderNotFound, pay.Error);
        Assert.Equal(0m, await _fixture.GetSettled.HandleAsync(new GetOrderSettledAmountQuery(order, "XOF")));
    }

    [Fact]
    public async Task A_payment_is_taken_at_the_location_of_its_order_with_the_chosen_method()
    {
        var started = await OnlineAsync(_fixture.GivenOrder());
        var cash = await CashAsync(_fixture.GivenOrder());

        var online = await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(started.PaymentId));
        var counter = await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(cash));

        Assert.Equal((_fixture.Location.Value, PaymentMethodType.Card), (online.LocationId, online.Method));
        // Un seul moyen chez ce prestataire : il n'est pas besoin de le préciser.
        Assert.Equal(PaymentMethodType.Cash, counter.Method);
    }

    [Fact]
    public async Task A_provider_with_several_methods_needs_the_method_to_be_named()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(_fixture.GivenOrder(), "fakepay")));

        Assert.Equal(PaymentsApplicationErrors.PaymentMethodRequired, refused.Error);
        Assert.Empty(_fixture.Gateway.Started);
    }

    [Fact]
    public async Task A_method_not_assigned_to_the_location_of_the_order_takes_no_payment_and_calls_no_provider()
    {
        _fixture.Configuration(_fixture.TenantA, "fakepay").AssignToLocation(
            new LocationReference(_fixture.TenantA, LocationId.New(), CountryCode.Create("BJ")), PaymentMethodType.Card, priority: 0, _fixture.Clock);

        var refused = await Assert.ThrowsAsync<DomainException>(() => OnlineAsync(_fixture.GivenOrder()));

        Assert.Equal(PaymentConfigurationErrors.NotAssignedToLocation, refused.Error);
        Assert.Empty(_fixture.Gateway.Started);
        Assert.Empty(_fixture.Payments);
    }

    [Fact]
    public async Task An_amount_outside_the_bounds_of_the_location_is_refused()
    {
        _fixture.Configuration(_fixture.TenantA, "fakepay").AssignToLocation(
            new LocationReference(_fixture.TenantA, _fixture.Location, CountryCode.Create("BJ")),
            PaymentMethodType.Card,
            priority: 0,
            _fixture.Clock,
            minAmount: Money.Of(500m, Currency.XOF),
            maxAmount: Money.Of(5000m, Currency.XOF));
        var order = _fixture.GivenOrder(total: 7000m);

        var tooMuch = await Assert.ThrowsAsync<DomainException>(() => OnlineAsync(order));
        var tooLittle = await Assert.ThrowsAsync<DomainException>(() => OnlineAsync(order, 100m));
        await OnlineAsync(order, 5000m);

        Assert.Equal(PaymentConfigurationErrors.AmountOutOfBounds, tooMuch.Error);
        Assert.Equal(PaymentConfigurationErrors.AmountOutOfBounds, tooLittle.Error);
        Assert.Single(_fixture.Payments);
    }

    [Fact]
    public async Task A_provider_failure_code_and_a_refund_reference_reach_the_payment()
    {
        var order = _fixture.GivenOrder();
        var first = await OnlineAsync(order);
        await _fixture.Apply.HandleAsync(new ApplyProviderResultCommand(
            "fakepay", ProviderResultKind.Failed, first.ProviderReference, Reason: "Do not honor", FailureCode: "DO_NOT_HONOR"));
        var retry = await _fixture.Retry.HandleAsync(new RetryPaymentCommand(first.PaymentId));
        await ResultAsync(ProviderResultKind.Captured, retry.ProviderReference, 7000m);
        var refundId = await _fixture.Refund.HandleAsync(new RequestRefundCommand(first.PaymentId, "Cold dish", 1000m));

        await _fixture.Apply.HandleAsync(new ApplyProviderResultCommand(
            "fakepay", ProviderResultKind.RefundSucceeded, retry.ProviderReference, RefundId: refundId, RefundReference: "re_77"));
        var dto = await _fixture.GetById.HandleAsync(new GetPaymentByIdQuery(first.PaymentId));

        Assert.Equal(("do_not_honor", "Do not honor"), (dto.Attempts.First().FailureCode, dto.Attempts.First().FailureReason));
        var refund = Assert.Single(dto.Refunds);
        Assert.Equal((RefundStatus.Succeeded, "re_77"), (refund.Status, refund.ProviderReference));
        Assert.NotNull(refund.CompletedAtUtc);
    }
}

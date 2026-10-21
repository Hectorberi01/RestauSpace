using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;

namespace Payments.Application;

public sealed class DepositPaymentTests
{
    private readonly PaymentsFixture _fixture = new();

    private RecordOfflineDepositHandler RecordDeposit => new(_fixture.Workspace, _fixture, _fixture, _fixture.Clock);

    private StartDepositPaymentHandler StartDeposit => new(_fixture.Workspace, _fixture, _fixture, _fixture.Clock);

    private ApplyDepositHandler ApplyDeposit => new(_fixture.Workspace, _fixture, _fixture.Clock);

    private RefundDepositPaymentsHandler RefundDeposit => new(_fixture.Workspace, _fixture.Refund, _fixture.Clock);

    private GetDepositPaymentsHandler DepositPayments => new(_fixture.Workspace);

    private Guid GivenDeposit(decimal amount = 20000m, bool acceptsPayment = true)
    {
        var reservation = Guid.CreateVersion7();
        _fixture.Deposits[reservation] = (_fixture.TenantA, new PaymentDeposit(reservation, _fixture.Location, Money.Of(amount, Currency.XOF), acceptsPayment));
        return reservation;
    }

    [Fact]
    public async Task A_deposit_taken_by_the_staff_is_held_for_its_reservation_not_for_an_order()
    {
        var reservation = GivenDeposit();

        var id = await RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(reservation, "cash", "T-42"));

        var payment = _fixture.Stored(id);
        Assert.Equal((PaymentPurpose.ReservationDeposit, reservation, PaymentStatus.Captured), (payment.Purpose, payment.ReservationId, payment.Status));
        Assert.True(payment.IsUnappliedDeposit);
        Assert.Equal(20000m, payment.Amount.Amount);
        var summary = await DepositPayments.HandleAsync(new GetDepositPaymentsQuery(reservation));
        Assert.Equal(20000m, summary.Settled);
        var again = await Assert.ThrowsAsync<DomainException>(() => RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(reservation, "cash", "T-43")));
        Assert.Equal(PaymentsApplicationErrors.NothingDue, again.Error);
    }

    [Fact]
    public async Task A_deposit_is_paid_online_or_refused_when_not_expected_or_by_gift_card()
    {
        var reservation = GivenDeposit();
        var closed = GivenDeposit(acceptsPayment: false);

        var started = await StartDeposit.HandleAsync(new StartDepositPaymentCommand(reservation, "fakepay", PaymentMethodType.Card));
        var notExpected = await Assert.ThrowsAsync<DomainException>(() => StartDeposit.HandleAsync(new StartDepositPaymentCommand(closed, "fakepay", PaymentMethodType.Card)));
        var giftCard = await Assert.ThrowsAsync<DomainException>(() => RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(reservation, GiftCards.ProviderCode, "CODE")));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(Guid.NewGuid(), "cash", "T")));

        Assert.Equal(PaymentStatus.Pending, _fixture.Stored(started.PaymentId).Status);
        Assert.Equal(PaymentsApplicationErrors.DepositNotPayable, notExpected.Error);
        Assert.Equal(PaymentErrors.DepositMethodNotAllowed, giftCard.Error);
        Assert.Equal(PaymentsApplicationErrors.DepositNotFound, unknown.Error);
    }

    [Fact]
    public async Task At_the_meal_the_deposit_is_deducted_from_the_bill_of_the_same_location()
    {
        var reservation = GivenDeposit();
        var id = await RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(reservation, "cash", "T-42"));
        var small = _fixture.GivenOrder(15000m);
        var elsewhere = _fixture.GivenOrder(50000m, location: LocationId.New());
        var bill = _fixture.GivenOrder(45000m);

        var tooLarge = await Assert.ThrowsAsync<DomainException>(() => ApplyDeposit.HandleAsync(new ApplyDepositCommand(reservation, small)));
        var otherLocation = await Assert.ThrowsAsync<DomainException>(() => ApplyDeposit.HandleAsync(new ApplyDepositCommand(reservation, elsewhere)));
        var applied = await ApplyDeposit.HandleAsync(new ApplyDepositCommand(reservation, bill));

        Assert.Equal(PaymentErrors.DepositAboveAmountDue, tooLarge.Error);
        Assert.Equal(PaymentErrors.DepositOtherLocation, otherLocation.Error);
        Assert.Equal(20000m, applied);
        var payment = _fixture.Stored(id);
        Assert.Equal(bill, payment.OrderId.Value);
        Assert.False(payment.IsUnappliedDeposit);
        DomainAssert.Raised<DepositApplied>(payment);
        var nothingLeft = await Assert.ThrowsAsync<DomainException>(() => ApplyDeposit.HandleAsync(new ApplyDepositCommand(reservation, bill)));
        Assert.Equal(PaymentsApplicationErrors.NoDepositToApply, nothingLeft.Error);
    }

    [Fact]
    public async Task Giving_the_deposit_back_refunds_what_was_collected_and_cancels_a_pending_payment()
    {
        var paid = GivenDeposit();
        var id = await RecordDeposit.HandleAsync(new RecordOfflineDepositCommand(paid, "cash", "T-42"));
        var pending = GivenDeposit();
        var started = await StartDeposit.HandleAsync(new StartDepositPaymentCommand(pending, "fakepay", PaymentMethodType.Card));

        await RefundDeposit.HandleAsync(new RefundDepositPaymentsCommand(paid, null));
        await RefundDeposit.HandleAsync(new RefundDepositPaymentsCommand(pending, null));

        Assert.Equal(PaymentStatus.Refunded, _fixture.Stored(id).Status);
        Assert.Equal(PaymentStatus.Cancelled, _fixture.Stored(started.PaymentId).Status);
    }
}

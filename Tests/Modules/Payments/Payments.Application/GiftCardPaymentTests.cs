using Payments.Application.Commands;
using Payments.Domain;

namespace Payments.Application;

public sealed class GiftCardPaymentTests
{
    private readonly PaymentsFixture _fixture = new();

    public GiftCardPaymentTests() => _fixture.Accept(_fixture.TenantA, GiftCards.ProviderCode);

    private Task<Guid> PayAsync(Guid order, string code, decimal? amount = null) =>
        _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, GiftCards.ProviderCode, code, amount));

    [Fact]
    public void The_gift_card_provider_is_built_in_on_site()
    {
        var provider = _fixture.Provider(GiftCards.ProviderCode);
        Assert.Equal(PaymentProviderType.OnSite, provider.Type);
        Assert.True(PaymentMethodType.GiftCard.IsOnSiteOnly());
    }

    [Fact]
    public async Task Paying_with_a_card_debits_it_and_keeps_only_card_and_movement_ids()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 10_000m);
        var order = _fixture.GivenOrder(7000m);

        var id = await PayAsync(order, code);

        var payment = _fixture.Stored(id);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(PaymentMethodType.GiftCard, payment.Method);
        Assert.Equal(3_000m, _fixture.StoredValue.BalanceOf(code));
        var reference = payment.Attempts.Single().ProviderReference.Value;
        Assert.DoesNotContain(code, reference, StringComparison.Ordinal);
        Assert.Equal(_fixture.StoredValue.Cards[code].Id, GiftCards.Parse(reference)!.Value.CardId);
    }

    [Fact]
    public async Task A_partial_payment_leaves_the_rest_due()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 2_000m);
        var order = _fixture.GivenOrder(7000m);

        await PayAsync(order, code, 2_000m);

        Assert.Equal(0m, _fixture.StoredValue.BalanceOf(code));
        await _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(order, "cash", "T-1"));
        Assert.Equal(2, _fixture.Payments.Count);
    }

    [Fact]
    public async Task A_card_with_too_little_left_is_refused_and_nothing_is_recorded()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 1_000m);

        await Assert.ThrowsAsync<DomainException>(() => PayAsync(_fixture.GivenOrder(7000m), code));

        Assert.Empty(_fixture.Payments);
        Assert.Equal(1_000m, _fixture.StoredValue.BalanceOf(code));
    }

    [Fact]
    public async Task The_code_is_required()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => PayAsync(_fixture.GivenOrder(), " "));
        Assert.Equal(PaymentsApplicationErrors.GiftCardCodeRequired, refused.Error);
    }

    [Fact]
    public async Task A_card_of_another_restaurant_is_not_found()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantB, 10_000m);
        await Assert.ThrowsAsync<NotFoundException>(() => PayAsync(_fixture.GivenOrder(), code));
    }

    [Fact]
    public async Task If_the_payment_cannot_be_saved_the_debit_is_reversed()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 10_000m);
        _fixture.FailingSaves = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => PayAsync(_fixture.GivenOrder(7000m), code));

        Assert.Equal(10_000m, _fixture.StoredValue.BalanceOf(code));
        Assert.Empty(_fixture.StoredValue.Movements);
    }

    [Fact]
    public async Task A_refund_goes_back_onto_the_card()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 10_000m);
        var id = await PayAsync(_fixture.GivenOrder(7000m), code);

        await _fixture.Refund.HandleAsync(new RequestRefundCommand(id, "Erreur", 2_000m));

        Assert.Equal(5_000m, _fixture.StoredValue.BalanceOf(code));
        var payment = _fixture.Stored(id);
        Assert.Equal(Money.Of(2_000m, Currency.XOF), payment.RefundedAmount);
    }

    [Fact]
    public async Task If_the_refund_cannot_be_saved_the_credit_is_reversed()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 10_000m);
        var id = await PayAsync(_fixture.GivenOrder(7000m), code);
        _fixture.FailingSaves = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Refund.HandleAsync(new RequestRefundCommand(id, "Erreur")));

        Assert.Equal(3_000m, _fixture.StoredValue.BalanceOf(code));
    }

    [Fact]
    public async Task A_cash_payment_can_be_refunded_as_store_credit()
    {
        var id = await _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(_fixture.GivenOrder(7000m), "cash", "T-1"));

        var refund = await _fixture.RefundAsStoreCredit.HandleAsync(new RefundAsStoreCreditCommand(id, "Plat manquant", 2_500m));

        Assert.Equal(2_500m, refund.Amount);
        Assert.Equal(2_500m, _fixture.StoredValue.BalanceOf(refund.Code));
        var payment = _fixture.Stored(id);
        Assert.Equal(RefundStatus.Succeeded, payment.Refunds.Single().Status);
    }

    [Fact]
    public async Task An_online_payment_refunded_as_store_credit_never_reaches_the_provider()
    {
        var order = _fixture.GivenOrder(7000m);
        var started = await _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(order, "fakepay", null, PaymentMethodType.Card));
        await _fixture.Apply.HandleAsync(new ApplyProviderResultCommand("fakepay", ProviderResultKind.Captured, started.ProviderReference, 7000m, "XOF"));

        await _fixture.RefundAsStoreCredit.HandleAsync(new RefundAsStoreCreditCommand(started.PaymentId, "Retard"));

        var payment = _fixture.Stored(started.PaymentId);
        Assert.Equal(Money.Of(7000m, Currency.XOF), payment.RefundedAmount);
        Assert.Equal(RefundStatus.Succeeded, payment.Refunds.Single().Status);
    }

    [Fact]
    public async Task A_gift_card_payment_is_not_refunded_as_a_new_store_credit()
    {
        var code = _fixture.StoredValue.Give(_fixture.TenantA, 10_000m);
        var id = await PayAsync(_fixture.GivenOrder(7000m), code);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.RefundAsStoreCredit.HandleAsync(new RefundAsStoreCreditCommand(id, "x")));
        Assert.Equal(PaymentsApplicationErrors.StoreCreditNotForGiftCard, refused.Error);
    }

    [Fact]
    public async Task If_the_store_credit_refund_cannot_be_saved_the_credit_is_cancelled()
    {
        var id = await _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(_fixture.GivenOrder(7000m), "cash", "T-1"));
        _fixture.FailingSaves = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.RefundAsStoreCredit.HandleAsync(new RefundAsStoreCreditCommand(id, "x")));

        Assert.Single(_fixture.StoredValue.Cancelled);
    }

    [Fact]
    public void References_round_trip()
    {
        var card = Guid.NewGuid();
        var movement = Guid.NewGuid();
        Assert.Equal((card, movement), GiftCards.Parse(GiftCards.PaymentReference(card, movement)));
        Assert.Null(GiftCards.Parse("T-12"));
        Assert.Null(GiftCards.Parse("gc:bad"));
        Assert.True(GiftCards.PaymentReference(card, movement).Length <= ProviderReference.MaxLength);
    }
}

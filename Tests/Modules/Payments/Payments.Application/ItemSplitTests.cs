using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;

namespace Payments.Application;

/// <summary>Addition partagée par article : chacun paie ses lignes, le montant vient des lignes, jamais d'une saisie.</summary>
public sealed class ItemSplitTests
{
    private readonly PaymentsFixture _fixture = new();
    private readonly Guid _poulet = Guid.NewGuid();
    private readonly Guid _bissap = Guid.NewGuid();

    private Guid GivenTableBill()
    {
        var order = _fixture.GivenOrder(10000m);
        var id = OrderId.From(order);
        var (tenant, current) = _fixture.Orders[id];
        _fixture.Orders[id] = (tenant, current with
        {
            Lines = [new PaymentOrderLine(_poulet, 2, Money.Of(9000m, Currency.XOF)), new PaymentOrderLine(_bissap, 3, Money.Of(1000m, Currency.XOF))],
        });
        return order;
    }

    private Task<Guid> PayAsync(Guid order, string reference, params (Guid Item, int Quantity)[] items) =>
        _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(
            order, "cash", reference, Items: items.Select(item => new PaymentItemShareInput(item.Item, item.Quantity)).ToList()));

    [Fact]
    public async Task Each_guest_pays_the_lines_he_had_and_the_shares_add_up_to_the_lines()
    {
        var order = GivenTableBill();

        var first = await PayAsync(order, "A", (_poulet, 1), (_bissap, 1));
        var second = await PayAsync(order, "B", (_bissap, 1));
        var third = await PayAsync(order, "C", (_poulet, 1), (_bissap, 1));

        Assert.Equal(4500m + 333m, _fixture.Stored(first).Amount.Amount);
        Assert.Equal(334m, _fixture.Stored(second).Amount.Amount);
        Assert.Equal(4500m + 333m, _fixture.Stored(third).Amount.Amount);
        Assert.Equal(10000m, _fixture.Payments.Sum(payment => payment.Amount.Amount));
        Assert.Equal(new PaymentItemShare(_bissap, 1), _fixture.Stored(second).CoveredItems.Single());
    }

    [Fact]
    public async Task Units_already_paid_unknown_lines_and_an_amount_with_lines_are_refused()
    {
        var order = GivenTableBill();
        await PayAsync(order, "A", (_poulet, 2));

        var twice = await Assert.ThrowsAsync<DomainException>(() => PayAsync(order, "B", (_poulet, 1)));
        var unknown = await Assert.ThrowsAsync<DomainException>(() => PayAsync(order, "C", (Guid.NewGuid(), 1)));
        var both = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordOffline.HandleAsync(
            new RecordOfflinePaymentCommand(order, "cash", "D", 500m, Items: [new PaymentItemShareInput(_bissap, 1)])));

        Assert.Equal(PaymentsApplicationErrors.ItemAlreadyPaid, twice.Error);
        Assert.Equal(PaymentsApplicationErrors.ItemNotOnOrder, unknown.Error);
        Assert.Equal(PaymentsApplicationErrors.ItemsWithAmount, both.Error);
    }
}

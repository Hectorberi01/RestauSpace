using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;

namespace Orders.Application;

public sealed class OrderQuoteTests
{
    private readonly OrdersFixture _fixture = new();

    [Fact]
    public async Task A_quote_prices_the_cart_exactly_like_the_order_it_will_become_and_saves_nothing()
    {
        var chicken = _fixture.GivenProduct(unitPrice: 3500m);
        var juice = _fixture.GivenProduct("Bissap", 500m);
        IReadOnlyList<QuoteItemInput> cart = [new(chicken, 2, ModifierIds: [Guid.NewGuid()]), new(juice, 1, Note: "Sans glace")];

        var quote = await _fixture.Quote.HandleAsync(new QuoteOrderQuery(_fixture.Location, OrderType.Takeaway, cart));
        var order = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway));
        foreach (var item in cart)
        {
            await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(order, item.ProductId, item.Quantity, item.VariantId, item.ModifierIds, item.Note));
        }

        var real = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(order));

        Assert.Equal((real.SubTotal, real.TaxTotal, real.Total), (quote.SubTotal, quote.TaxTotal, quote.Total));
        Assert.Equal(7510m, quote.Total);
        Assert.Equal(("XOF", PriceTaxMode.TaxInclusive), (quote.CurrencyCode, quote.PriceTaxMode));
        Assert.Equal("Sans glace", quote.Items[1].Note);
        Assert.Single(_fixture.Orders.Items);
    }

    [Fact]
    public async Task A_quote_applies_a_coupon_discount_and_a_delivery_fee_like_the_order_will()
    {
        var chicken = _fixture.GivenProduct(unitPrice: 3500m);
        IReadOnlyList<QuoteItemInput> cart = [new(chicken, 2)];
        var promotion = Guid.NewGuid().ToString();

        var quote = await _fixture.Quote.HandleAsync(new QuoteOrderQuery(
            _fixture.Location, OrderType.Delivery, cart, [new QuoteDiscountInput(DiscountSource.Promotion, 1000m, promotion, "Coupon BIENVENUE")], 1500m));
        var order = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Delivery));
        await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(order, chicken, 2));
        await _fixture.ApplyExternalDiscount.HandleAsync(new ApplyExternalDiscountCommand(order, DiscountSource.Promotion, 1000m, "XOF", promotion));
        await _fixture.ApplyDeliveryFee.HandleAsync(new ApplyDeliveryFeeCommand(order, 1500m, "XOF", Guid.NewGuid().ToString("D")));
        var real = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(order));

        Assert.Equal((real.SubTotal, real.DiscountTotal, real.TaxTotal, real.Total), (quote.SubTotal, quote.DiscountTotal, quote.TaxTotal, quote.Total));
        Assert.Equal((7500m, (decimal?)1500m), (quote.Total, quote.DeliveryFee));
    }

    [Fact]
    public async Task A_quote_refuses_an_unknown_product_a_location_of_another_tenant_or_a_bad_quantity()
    {
        var foreignLocation = _fixture.GivenLocation(_fixture.TenantB);
        var product = _fixture.GivenProduct();

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Quote.HandleAsync(new QuoteOrderQuery(_fixture.Location, OrderType.Takeaway, [new QuoteItemInput(Guid.NewGuid(), 1)])));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Quote.HandleAsync(new QuoteOrderQuery(foreignLocation, OrderType.Takeaway, [new QuoteItemInput(product, 1)])));
        var quantity = await Assert.ThrowsAnyAsync<DomainException>(() =>
            _fixture.Quote.HandleAsync(new QuoteOrderQuery(_fixture.Location, OrderType.Takeaway, [new QuoteItemInput(product, 0)])));

        Assert.Equal(OrdersApplicationErrors.ProductNotFound, unknown.Error);
        Assert.Equal(OrdersApplicationErrors.LocationNotFound, foreign.Error);
        Assert.NotNull(quantity);
        Assert.Empty(_fixture.Orders.Items);
    }
}

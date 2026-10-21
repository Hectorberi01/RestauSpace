namespace Orders.Domain;

public class OrderDeliveryFeeTests
{
    [Fact]
    public void A_delivery_fee_is_added_to_the_total_without_tax_and_can_be_replayed()
    {
        var order = Given.DraftOrder(type: OrderType.Delivery);
        order.AddItem(Given.Product(Given.Eur(10.00m), Given.Vat20), Given.Qty(2));

        order.ApplyDeliveryFee(Given.Eur(3.00m), "fulfillment-1", new FakeClock());
        order.ApplyDeliveryFee(Given.Eur(3.00m), "fulfillment-1", new FakeClock());

        Assert.Equal(Given.Eur(4.00m), order.TaxTotal);
        Assert.Equal(Given.Eur(27.00m), order.Total);
        Assert.Equal(Given.Eur(3.00m), order.DeliveryFee);
        Assert.Single(order.DomainEvents.OfType<OrderDeliveryFeeChanged>());
    }

    [Fact]
    public void The_fee_survives_line_changes_and_is_never_discounted()
    {
        var order = Given.DraftOrder(type: OrderType.Delivery, mode: PriceTaxMode.TaxInclusive);
        order.AddItem(Given.Product(Given.Eur(12.00m), Given.Vat20), Given.Qty(1));
        order.ApplyDeliveryFee(Given.Eur(2.50m), "fulfillment-1", new FakeClock());

        order.ApplyDiscount(Given.Discount(Given.Eur(20.00m)));

        Assert.Equal(Given.Eur(12.00m), order.DiscountTotal);
        Assert.Equal(Given.Eur(2.50m), order.Total);
    }

    [Fact]
    public void Only_the_fulfillment_that_set_the_fee_removes_it()
    {
        var order = Given.DraftOrder(type: OrderType.Delivery);
        order.AddItem(Given.Product(Given.Eur(10.00m), Given.Vat20), Given.Qty(1));
        order.ApplyDeliveryFee(Given.Eur(3.00m), "fulfillment-2", new FakeClock());

        order.RemoveDeliveryFee("fulfillment-1", new FakeClock());
        var kept = order.Total;
        order.RemoveDeliveryFee("fulfillment-2", new FakeClock());

        Assert.Equal(Given.Eur(15.00m), kept);
        Assert.Null(order.DeliveryFee);
        Assert.Equal(Given.Eur(12.00m), order.Total);
    }

    [Fact]
    public void A_fee_needs_an_open_delivery_order_in_its_currency()
    {
        var takeaway = Given.DraftOrder();
        var delivery = Given.DraftOrder(type: OrderType.Delivery);

        DomainAssert.Throws(OrderErrors.DeliveryFeeRequiresDelivery, () => takeaway.ApplyDeliveryFee(Given.Eur(1m), "f", new FakeClock()));
        DomainAssert.Throws(OrderErrors.CurrencyMismatch, () => delivery.ApplyDeliveryFee(Given.Xof(500m), "f", new FakeClock()));
        DomainAssert.Throws(OrderErrors.DeliveryFeeNegative, () => delivery.ApplyDeliveryFee(Given.Eur(-1m), "f", new FakeClock()));
        DomainAssert.Throws(OrderErrors.DeliveryFeeReferenceInvalid, () => delivery.ApplyDeliveryFee(Given.Eur(1m), " ", new FakeClock()));
    }
}

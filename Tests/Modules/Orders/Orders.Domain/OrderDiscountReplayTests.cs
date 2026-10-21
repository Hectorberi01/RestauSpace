namespace Orders.Domain;

public sealed class OrderDiscountReplayTests
{
    [Fact]
    public void A_discount_already_on_a_placed_order_is_reported_as_already_applied_not_as_frozen()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(2m), "promo-1", DiscountSource.Promotion));
        order.Place(new FakeClock());

        DomainAssert.Throws(OrderErrors.DiscountAlreadyApplied, () => order.ApplyDiscount(Given.Discount(Given.Eur(2m), "PROMO-1", DiscountSource.Promotion)));
        DomainAssert.Throws(OrderErrors.NotEditable, () => order.ApplyDiscount(Given.Discount(Given.Eur(2m), "promo-2", DiscountSource.Promotion)));
    }
}

using System.Reflection;

namespace Orders.Domain;

/// <summary>
/// Aucun mutateur public ne peut laisser la commande modifiée quand il lève une exception : chaque test compare un
/// instantané complet des lignes, des remises et des totaux pris avant et après l'appel en échec.
/// </summary>
public class OrderAtomicityTests
{
    private static readonly TaxSnapshot FullTax = TaxSnapshot.Create("FULL", "100 %", 1m);

    private static string Snapshot(Order order) =>
        string.Join(
            " | ",
            order.Status,
            order.SubTotal,
            order.DiscountTotal,
            order.TaxTotal,
            order.Total,
            string.Join(
                " ; ",
                order.Items.Select(item => string.Join(
                    "/",
                    item.Id,
                    item.ProductId,
                    item.VariantId,
                    item.Quantity,
                    item.Note,
                    item.UnitPrice,
                    item.DiscountAmount,
                    item.NetAmount,
                    item.TaxAmount,
                    string.Join(",", item.Modifiers.Select(modifier => $"{modifier.ModifierId}:{modifier.Name}:{modifier.PriceDelta}"))))),
            string.Join(" ; ", order.Discounts.Select(discount => $"{discount.Source}:{discount.Reference}:{discount.Amount}")),
            order.DomainEvents.Count);

    private static void AssertRejectedWithoutAnyChange(Order order, DomainError expected, Action failingCall)
    {
        var before = Snapshot(order);

        DomainAssert.Throws(expected, failingCall);

        Assert.Equal(before, Snapshot(order));
    }

    /// <summary>
    /// Donne à une ligne un prix unitaire qu'aucun point d'entrée n'accepte plus, comme pourrait en contenir une commande
    /// persistée avant l'existence des plafonds, puis laisse la commande recalculer ses totaux : seul moyen de faire
    /// échouer le recalcul lui-même.
    /// </summary>
    private static void InflateUnitPrice(Order order, OrderItemId itemId, decimal amount)
    {
        var item = order.Items.Single(line => line.Id == itemId);
        typeof(OrderItem).GetProperty(nameof(OrderItem.UnitPrice))!.SetValue(item, Money.Of(amount, order.Currency));
        typeof(Order).GetMethod("Recalculate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(order, null);
    }

    /// <summary>Un brouillon : une ligne classique (2.00 avec option à -1.00, x1), une deuxième ligne et une remise.</summary>
    private static (Order Order, OrderItemId Burger, OrderItemId Soda, OrderItemModifier Rebate) Basket(
        PriceTaxMode mode = PriceTaxMode.TaxInclusive, TaxSnapshot? sodaTax = null)
    {
        var order = Given.DraftOrder(mode: mode);
        var rebate = Given.Modifier("No bun", Given.Eur(-1m));
        var burger = order.AddItem(Given.Product(Given.Eur(2m), Given.NoTax, "Burger", null, rebate), Given.Qty(1), "Well done");
        var soda = order.AddItem(Given.Product(Given.Eur(3m), sodaTax ?? Given.NoTax, "Soda"), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(1m)));
        return (order, burger, soda, rebate);
    }

    [Fact]
    public void AddItem_that_fails_leaves_the_order_untouched()
    {
        var (order, _, _, _) = Basket();
        var cheese = Given.Modifier("Cheese", Given.Eur(1m));

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.CurrencyMismatch, () => order.AddItem(Given.Product(Given.Xof(100m)), Given.Qty(1)));
        AssertRejectedWithoutAnyChange(
            order, CommonErrors.TenantMismatch, () => order.AddItem(Given.Product(Given.Eur(1m), tenant: TenantId.New()), Given.Qty(1)));
        AssertRejectedWithoutAnyChange(
            order,
            OrderErrors.ItemModifierDuplicate,
            () => order.AddItem(Given.Product(Given.Eur(1m), null, "Burger", null, cheese, cheese), Given.Qty(1)));
        AssertRejectedWithoutAnyChange(
            order,
            OrderErrors.ItemNoteTooLong,
            () => order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1), new string('n', OrderItem.MaxNoteLength + 1)));
    }

    [Fact]
    public void AddItem_whose_recalculation_overflows_adds_no_phantom_line()
    {
        var (order, _, soda, _) = Basket();
        InflateUnitPrice(order, soda, Money.MaxAbsoluteAmount - 1m);
        Assert.Equal(Given.Eur(Money.MaxAbsoluteAmount), order.SubTotal);

        AssertRejectedWithoutAnyChange(
            order, CommonErrors.MoneyOutOfRange, () => order.AddItem(Given.Product(Given.Eur(1m)), Given.Qty(1)));

        // La commande est exactement telle qu'avant : la passer publie des totaux conformes à ses deux lignes.
        order.Place(new FakeClock());
        var placed = DomainAssert.Raised<OrderPlaced>(order);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(Given.Eur(Money.MaxAbsoluteAmount), placed.SubTotal);
        Assert.Equal(Given.Eur(Money.MaxAbsoluteAmount - 1m), placed.Total);
    }

    [Fact]
    public void ChangeItemQuantity_that_fails_leaves_the_order_untouched()
    {
        var (order, _, soda, _) = Basket();

        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemNotFound, () => order.ChangeItemQuantity(OrderItemId.New(), Given.Qty(2)));

        InflateUnitPrice(order, soda, Money.MaxAbsoluteAmount / 2m);
        AssertRejectedWithoutAnyChange(order, CommonErrors.MoneyOutOfRange, () => order.ChangeItemQuantity(soda, Given.Qty(3)));
        Assert.Equal(1, order.Items.Single(item => item.Id == soda).Quantity.Value);
    }

    [Fact]
    public void ChangeItemNote_that_fails_leaves_the_order_untouched()
    {
        var (order, burger, _, _) = Basket();

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.ItemNoteTooLong, () => order.ChangeItemNote(burger, new string('n', OrderItem.MaxNoteLength + 1)));
        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemNotFound, () => order.ChangeItemNote(OrderItemId.New(), "No onion"));
    }

    [Fact]
    public void AddItemModifier_that_fails_leaves_the_order_untouched()
    {
        var (order, burger, soda, rebate) = Basket();

        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemModifierDuplicate, () => order.AddItemModifier(burger, rebate));
        AssertRejectedWithoutAnyChange(
            order, OrderErrors.ItemUnitPriceNegative, () => order.AddItemModifier(burger, Given.Modifier("Too generous", Given.Eur(-1.01m))));
        AssertRejectedWithoutAnyChange(
            order,
            OrderErrors.ItemUnitPriceTooLarge,
            () => order.AddItemModifier(burger, Given.Modifier("Gold leaf", Given.Eur(ProductSnapshot.MaxUnitPriceAmount))));
        AssertRejectedWithoutAnyChange(
            order, OrderErrors.CurrencyMismatch, () => order.AddItemModifier(burger, Given.Modifier("Sauce", Given.Xof(100m))));
        AssertRejectedWithoutAnyChange(
            order, OrderErrors.ItemNotFound, () => order.AddItemModifier(OrderItemId.New(), Given.Modifier("Sauce", Given.Eur(1m))));

        // Le burger vaut 1.00 : avec l'autre ligne à max - 1, le sous-total vaut pile le maximum, une unité de plus déborde.
        InflateUnitPrice(order, soda, Money.MaxAbsoluteAmount - 1m);
        AssertRejectedWithoutAnyChange(
            order, CommonErrors.MoneyOutOfRange, () => order.AddItemModifier(burger, Given.Modifier("Cheese", Given.Eur(1m))));
        Assert.Equal(rebate, Assert.Single(order.Items.Single(item => item.Id == burger).Modifiers));
    }

    [Fact]
    public void RemoveItemModifier_that_fails_leaves_the_order_untouched()
    {
        var (order, burger, soda, rebate) = Basket();

        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemModifierNotFound, () => order.RemoveItemModifier(burger, ModifierId.New()));
        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemNotFound, () => order.RemoveItemModifier(OrderItemId.New(), rebate.ModifierId));

        // Retirer l'option à -1.00 fait passer le burger à 2.00 et le sous-total au-dessus du maximum.
        InflateUnitPrice(order, soda, Money.MaxAbsoluteAmount - 1m);
        AssertRejectedWithoutAnyChange(order, CommonErrors.MoneyOutOfRange, () => order.RemoveItemModifier(burger, rebate.ModifierId));
        Assert.Equal(rebate, Assert.Single(order.Items.Single(item => item.Id == burger).Modifiers));
    }

    [Fact]
    public void RemoveItem_that_fails_leaves_the_order_untouched()
    {
        var (order, _, _, _) = Basket();

        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemNotFound, () => order.RemoveItem(OrderItemId.New()));
    }

    [Fact]
    public void ApplyDiscount_that_fails_leaves_the_order_untouched()
    {
        var (order, _, _, _) = Basket();

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.DiscountAlreadyApplied, () => order.ApplyDiscount(Given.Discount(Given.Eur(2m), "welcome10")));
        AssertRejectedWithoutAnyChange(
            order, OrderErrors.CurrencyMismatch, () => order.ApplyDiscount(Given.Discount(Given.Xof(100m), "OTHER")));

        for (var i = order.Discounts.Count; i < Order.MaxDiscounts; i++)
        {
            order.ApplyDiscount(Given.Discount(Given.Eur(0.01m), $"D{i}"));
        }

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.TooManyDiscounts, () => order.ApplyDiscount(Given.Discount(Given.Eur(0.01m), "ONE-TOO-MANY")));
    }

    [Fact]
    public void RemoveDiscount_and_ClearDiscounts_that_fail_leave_the_order_untouched()
    {
        // Hors taxes à 100 % : le total vaut deux fois le montant remisé, donc supprimer la remise l'augmente.
        var (order, burger, soda, _) = Basket(PriceTaxMode.TaxExclusive, FullTax);
        order.RemoveItem(burger);

        AssertRejectedWithoutAnyChange(order, OrderErrors.DiscountNotFound, () => order.RemoveDiscount(DiscountSource.Coupon, "OTHER"));
        AssertRejectedWithoutAnyChange(order, OrderErrors.DiscountSourceUnknown, () => order.RemoveDiscount((DiscountSource)0, "WELCOME10"));

        // 2 x (max / 2 + 0.50 - 1.00) reste dans les bornes ; sans la remise de 1.00, on dépasse d'une unité.
        InflateUnitPrice(order, soda, (Money.MaxAbsoluteAmount / 2m) + 0.50m);
        Assert.Equal(Given.Eur(Money.MaxAbsoluteAmount - 1m), order.Total);

        AssertRejectedWithoutAnyChange(order, CommonErrors.MoneyOutOfRange, () => order.RemoveDiscount(DiscountSource.Coupon, "WELCOME10"));
        AssertRejectedWithoutAnyChange(order, CommonErrors.MoneyOutOfRange, order.ClearDiscounts);
        Assert.Equal(Given.Eur(1m), Assert.Single(order.Items).DiscountAmount);
    }

    [Fact]
    public void Unit_price_is_accepted_at_its_cap_and_rejected_above()
    {
        var atCap = Given.Product(Given.Eur(ProductSnapshot.MaxUnitPriceAmount));

        Assert.Equal(ProductSnapshot.MaxUnitPriceAmount, atCap.UnitPrice.Amount);
        DomainAssert.Throws(
            OrderErrors.ProductUnitPriceTooLarge, () => Given.Product(Given.Eur(ProductSnapshot.MaxUnitPriceAmount + 0.01m)));
    }

    [Fact]
    public void Modifier_delta_is_accepted_at_its_cap_in_both_directions_and_rejected_beyond()
    {
        Assert.Equal(OrderItemModifier.MaxPriceDeltaAmount, Given.Modifier("Up", Given.Eur(OrderItemModifier.MaxPriceDeltaAmount)).PriceDelta.Amount);
        Assert.Equal(-OrderItemModifier.MaxPriceDeltaAmount, Given.Modifier("Down", Given.Eur(-OrderItemModifier.MaxPriceDeltaAmount)).PriceDelta.Amount);
        DomainAssert.Throws(
            OrderErrors.ModifierPriceDeltaTooLarge, () => Given.Modifier("Up", Given.Eur(OrderItemModifier.MaxPriceDeltaAmount + 0.01m)));
        DomainAssert.Throws(
            OrderErrors.ModifierPriceDeltaTooLarge, () => Given.Modifier("Down", Given.Eur(-OrderItemModifier.MaxPriceDeltaAmount - 0.01m)));
    }

    [Fact]
    public void Discount_amount_is_accepted_at_its_cap_and_rejected_above()
    {
        Assert.Equal(OrderDiscount.MaxAmount, Given.Discount(Given.Eur(OrderDiscount.MaxAmount)).Amount.Amount);
        DomainAssert.Throws(OrderErrors.DiscountAmountTooLarge, () => Given.Discount(Given.Eur(OrderDiscount.MaxAmount + 0.01m)));
    }

    [Fact]
    public void Unit_price_with_modifiers_is_accepted_at_the_cap_and_rejected_above()
    {
        var order = Given.DraftOrder();
        var rebate = Given.Modifier("Rebate", Given.Eur(-1m));
        var line = order.AddItem(
            Given.Product(Given.Eur(ProductSnapshot.MaxUnitPriceAmount), null, "Truffle", null, rebate), Given.Qty(1));

        order.AddItemModifier(line, Given.Modifier("Back to the cap", Given.Eur(1m)));
        Assert.Equal(Given.Eur(ProductSnapshot.MaxUnitPriceAmount), Assert.Single(order.Items).UnitPriceWithModifiers);

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.ItemUnitPriceTooLarge, () => order.AddItemModifier(line, Given.Modifier("One cent", Given.Eur(0.01m))));
        AssertRejectedWithoutAnyChange(order, OrderErrors.ItemUnitPriceTooLarge, () => order.RemoveItemModifier(line, rebate.ModifierId));
    }

    [Fact]
    public void Line_accepts_the_maximum_number_of_modifiers_and_rejects_one_more()
    {
        var order = Given.DraftOrder();
        var modifiers = Enumerable.Range(0, OrderItem.MaxModifiers).Select(i => Given.Modifier($"Option {i}", Given.Eur(0.10m))).ToArray();
        var line = order.AddItem(Given.Product(Given.Eur(10m), null, "Burger", null, modifiers[..^1]), Given.Qty(1));

        order.AddItemModifier(line, modifiers[^1]);
        Assert.Equal(OrderItem.MaxModifiers, Assert.Single(order.Items).Modifiers.Count);

        AssertRejectedWithoutAnyChange(
            order, OrderErrors.ItemTooManyModifiers, () => order.AddItemModifier(line, Given.Modifier("One too many", Given.Eur(0.10m))));
        AssertRejectedWithoutAnyChange(
            order,
            OrderErrors.ItemTooManyModifiers,
            () => order.AddItem(
                Given.Product(Given.Eur(10m), null, "Burger", null, [.. modifiers, Given.Modifier("One too many", Given.Eur(0.10m))]),
                Given.Qty(1)));
    }

    [Fact]
    public void Order_accepts_the_maximum_number_of_discounts_and_rejects_one_more()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(100m), Given.NoTax), Given.Qty(1));
        for (var i = 0; i < Order.MaxDiscounts; i++)
        {
            order.ApplyDiscount(Given.Discount(Given.Eur(1m), $"D{i}"));
        }

        Assert.Equal(Order.MaxDiscounts, order.Discounts.Count);
        DomainAssert.Throws(OrderErrors.TooManyDiscounts, () => order.ApplyDiscount(Given.Discount(Given.Eur(1m), "ONE-TOO-MANY")));
        Assert.Equal(Given.Eur(100m - Order.MaxDiscounts), order.Total);
    }

    [Theory]
    [InlineData(PriceTaxMode.TaxExclusive)]
    [InlineData(PriceTaxMode.TaxInclusive)]
    public void Largest_order_the_caps_allow_stays_within_the_money_range(PriceTaxMode mode)
    {
        var order = Given.DraftOrder(mode: mode);
        var product = Given.Product(Given.Eur(ProductSnapshot.MaxUnitPriceAmount), FullTax);
        for (var i = 0; i < Order.MaxItems; i++)
        {
            order.AddItem(product, Given.Qty(Quantity.MaxValue));
        }

        var subTotal = Order.MaxItems * Quantity.MaxValue * ProductSnapshot.MaxUnitPriceAmount;
        Assert.Equal(Given.Eur(subTotal), order.SubTotal);
        Assert.Equal(Given.Eur(mode == PriceTaxMode.TaxExclusive ? 2m * subTotal : subTotal), order.Total);
        Assert.True(order.Total.Amount < Money.MaxAbsoluteAmount / 2m);

        for (var i = 0; i < Order.MaxDiscounts; i++)
        {
            order.ApplyDiscount(Given.Discount(Given.Eur(OrderDiscount.MaxAmount), $"D{i}"));
        }

        Assert.Equal(Given.Eur(Order.MaxDiscounts * OrderDiscount.MaxAmount), order.DiscountTotal);
        Assert.Equal(order.NetTotal + order.TaxTotal, order.Total);
    }
}

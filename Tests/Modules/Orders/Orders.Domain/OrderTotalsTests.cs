namespace Orders.Domain;

public class OrderTotalsTests
{
    [Fact]
    public void Totals_add_tax_on_top_of_tax_exclusive_prices()
    {
        var order = Given.DraftOrder();

        order.AddItem(Given.Product(Given.Eur(10.00m), Given.Vat20), Given.Qty(2));
        order.AddItem(Given.Product(Given.Eur(3.50m), Given.Vat10), Given.Qty(1));

        Assert.Equal(Given.Eur(23.50m), order.SubTotal);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(4.35m), order.TaxTotal);
        Assert.Equal(Given.Eur(27.85m), order.Total);
    }

    [Fact]
    public void Totals_with_modifiers_mixed_rates_and_a_discount_match_the_documented_formula()
    {
        var order = Given.DraftOrder();
        var burger = Given.Product(
            Given.Eur(9.99m), Given.Vat10, "Burger", null,
            Given.Modifier("Cheese", Given.Eur(1.50m)),
            Given.Modifier("No bun", Given.Eur(-0.49m)));
        order.AddItem(burger, Given.Qty(3));
        order.AddItem(Given.Product(Given.Eur(2.35m), Given.Vat20, "Soda"), Given.Qty(2));

        order.ApplyDiscount(Given.Discount(Given.Eur(5.00m)));

        // 500 x 3300 / 3770 = 437.67 -> 437 (+1 pour le plus grand reste) ; 500 x 470 / 3770 = 62.33 -> 62.
        var items = order.Items.ToArray();
        Assert.Equal(Given.Eur(37.70m), order.SubTotal);
        Assert.Equal(Given.Eur(5.00m), order.DiscountTotal);
        Assert.Equal(Given.Eur(4.38m), items[0].DiscountAmount);
        Assert.Equal(Given.Eur(0.62m), items[1].DiscountAmount);
        Assert.Equal(Given.Eur(28.62m), items[0].TaxableAmount);
        Assert.Equal(Given.Eur(4.08m), items[1].TaxableAmount);
        Assert.Equal(Given.Eur(2.86m), items[0].TaxAmount);
        Assert.Equal(Given.Eur(0.82m), items[1].TaxAmount);
        Assert.Equal(Given.Eur(3.68m), order.TaxTotal);
        Assert.Equal(Given.Eur(36.38m), order.Total);
    }

    [Fact]
    public void Discount_that_does_not_divide_evenly_loses_no_cent()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(0.10m), Given.Vat20);
        for (var i = 0; i < 3; i++)
        {
            order.AddItem(product, Given.Qty(1));
        }

        order.ApplyDiscount(Given.Discount(Given.Eur(0.10m)));

        // 10 centimes sur 3 lignes égales : 3.33 chacune -> 3, 3, 3 et le centime restant va à la première ligne
        // (même produit sur chaque ligne : seule la position les distingue).
        Assert.Equal(
            [Given.Eur(0.04m), Given.Eur(0.03m), Given.Eur(0.03m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        Assert.Equal(
            [Given.Eur(0.01m), Given.Eur(0.01m), Given.Eur(0.01m)],
            order.Items.Select(item => item.TaxAmount).ToArray());
        Assert.Equal(Given.Eur(0.30m), order.SubTotal);
        Assert.Equal(Given.Eur(0.10m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0.03m), order.TaxTotal);
        Assert.Equal(Given.Eur(0.23m), order.Total);
    }

    [Fact]
    public void Leftover_unit_goes_to_the_first_line_when_identical_lines_tie()
    {
        var order = Given.DraftOrder();
        var product = Given.Product(Given.Eur(1.00m), Given.NoTax);
        order.AddItem(product, Given.Qty(1));
        order.AddItem(product, Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(0.01m)));

        Assert.Equal(
            [Given.Eur(0.01m), Given.Eur(0m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        Assert.Equal(Given.Eur(1.99m), order.Total);
    }

    [Fact]
    public void Totals_in_a_zero_decimal_currency_stay_whole_and_lose_no_unit()
    {
        var order = Given.DraftOrder(Currency.XOF);
        order.AddItem(Given.Product(Given.Xof(1000m), Given.Vat18), Given.Qty(1));
        order.AddItem(Given.Product(Given.Xof(500m), Given.Vat18), Given.Qty(1));
        order.AddItem(Given.Product(Given.Xof(333m), Given.Vat18), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Xof(100m)));

        // Parts 54.56 / 27.28 / 18.17 -> 54, 27, 18 puis +1 sur le plus grand reste (première ligne).
        Assert.Equal(
            [Given.Xof(55m), Given.Xof(27m), Given.Xof(18m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        // 945 x 18 % = 170.1 ; 473 x 18 % = 85.14 ; 315 x 18 % = 56.7.
        Assert.Equal(
            [Given.Xof(170m), Given.Xof(85m), Given.Xof(57m)],
            order.Items.Select(item => item.TaxAmount).ToArray());
        Assert.Equal(Given.Xof(1833m), order.SubTotal);
        Assert.Equal(Given.Xof(100m), order.DiscountTotal);
        Assert.Equal(Given.Xof(312m), order.TaxTotal);
        Assert.Equal(Given.Xof(2045m), order.Total);
        Assert.Equal(order.Total.Amount, decimal.Truncate(order.Total.Amount));
    }

    [Fact]
    public void Totals_in_a_three_decimal_currency_keep_three_decimals()
    {
        var kwd = Currency.FromCode("KWD");
        var order = Given.DraftOrder(kwd);

        order.AddItem(Given.Product(Money.Of(0.333m, kwd), TaxSnapshot.Create("VAT5", "VAT 5 %", 0.05m)), Given.Qty(3));

        Assert.Equal(Money.Of(0.999m, kwd), order.SubTotal);
        Assert.Equal(Money.Of(0.050m, kwd), order.TaxTotal);
        Assert.Equal(Money.Of(1.049m, kwd), order.Total);
    }

    [Fact]
    public void Line_tax_is_rounded_half_away_from_zero()
    {
        var order = Given.DraftOrder();

        order.AddItem(Given.Product(Given.Eur(0.25m), Given.Vat10), Given.Qty(1));

        Assert.Equal(Given.Eur(0.03m), order.TaxTotal);
        Assert.Equal(Given.Eur(0.28m), order.Total);
    }

    [Fact]
    public void Tax_is_rounded_per_line_and_the_total_is_the_sum_of_line_taxes()
    {
        var order = Given.DraftOrder();
        for (var i = 0; i < 3; i++)
        {
            order.AddItem(Given.Product(Given.Eur(0.07m), Given.Vat20), Given.Qty(1));
        }

        // 0.014 par ligne s'arrondit à 0.01 : trois lignes donnent 0.03 (0.21 x 20 % arrondi globalement donnerait 0.04).
        Assert.Equal(Given.Eur(0.03m), order.TaxTotal);
        Assert.Equal(Given.Eur(0.24m), order.Total);
    }

    [Fact]
    public void Tax_is_computed_on_the_discounted_base()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(100m), Given.Vat20), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(25m)));

        Assert.Equal(Given.Eur(15m), order.TaxTotal);
        Assert.Equal(Given.Eur(90m), order.Total);
    }

    [Fact]
    public void Stacked_discounts_add_up()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(2m), "PROMO-LUNCH", DiscountSource.Promotion));
        order.ApplyDiscount(Given.Discount(Given.Eur(3m), "WELCOME10", DiscountSource.Coupon));

        Assert.Equal(2, order.Discounts.Count);
        Assert.Equal(Given.Eur(5m), order.DiscountTotal);
        Assert.Equal(Given.Eur(1m), order.TaxTotal);
        Assert.Equal(Given.Eur(6m), order.Total);
    }

    [Fact]
    public void Discount_equal_to_the_subtotal_brings_the_total_to_zero()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(12.34m), Given.Vat20), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(12.34m)));

        Assert.Equal(Given.Eur(12.34m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), order.TaxTotal);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void Discount_larger_than_the_subtotal_is_capped_so_the_total_never_goes_negative()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(0.01m), Given.Vat20), Given.Qty(1));
        order.AddItem(Given.Product(Given.Eur(29.99m), Given.Vat10), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(50m)));

        Assert.Equal(Given.Eur(50m), Assert.Single(order.Discounts).Amount);
        Assert.Equal(Given.Eur(30m), order.DiscountTotal);
        Assert.Equal(
            [Given.Eur(0.01m), Given.Eur(29.99m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        Assert.Equal(Given.Eur(0m), order.TaxTotal);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void Capped_discount_is_re_evaluated_when_lines_change()
    {
        var order = Given.DraftOrder();
        var first = order.AddItem(Given.Product(Given.Eur(30m), Given.NoTax), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(50m)));
        Assert.Equal(Given.Eur(30m), order.DiscountTotal);

        order.AddItem(Given.Product(Given.Eur(40m), Given.NoTax), Given.Qty(1));
        Assert.Equal(Given.Eur(50m), order.DiscountTotal);
        Assert.Equal(Given.Eur(20m), order.Total);

        order.RemoveItem(first);
        Assert.Equal(Given.Eur(40m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void Discount_on_an_empty_order_deducts_nothing_until_lines_are_added()
    {
        var order = Given.DraftOrder();

        order.ApplyDiscount(Given.Discount(Given.Eur(5m)));
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), order.Total);

        order.AddItem(Given.Product(Given.Eur(20m), Given.NoTax), Given.Qty(1));
        Assert.Equal(Given.Eur(5m), order.DiscountTotal);
        Assert.Equal(Given.Eur(15m), order.Total);
    }

    [Fact]
    public void Free_line_receives_no_discount_share()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(0m), Given.Vat20, "Tap water"), Given.Qty(2));
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(1m)));

        Assert.Equal(
            [Given.Eur(0m), Given.Eur(1m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        Assert.Equal(Given.Eur(10.80m), order.Total);
    }

    [Fact]
    public void ApplyDiscount_rejects_another_currency()
    {
        var order = Given.DraftOrder(Currency.EUR);
        order.AddItem(Given.Product(Given.Eur(10m)), Given.Qty(1));

        DomainAssert.Throws(OrderErrors.CurrencyMismatch, () => order.ApplyDiscount(Given.Discount(Given.Xof(100m))));

        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Eur(12m), order.Total);
    }

    [Fact]
    public void ApplyDiscount_rejects_the_same_origin_twice()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.NoTax), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(1m), "WELCOME10"));

        DomainAssert.Throws(OrderErrors.DiscountAlreadyApplied, () => order.ApplyDiscount(Given.Discount(Given.Eur(2m), "welcome10")));

        Assert.Equal(Given.Eur(1m), order.DiscountTotal);
    }

    [Fact]
    public void ApplyDiscount_accepts_the_same_reference_from_another_source()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.NoTax), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(1m), "X1", DiscountSource.Coupon));

        order.ApplyDiscount(Given.Discount(Given.Eur(2m), "X1", DiscountSource.Promotion));

        Assert.Equal(Given.Eur(3m), order.DiscountTotal);
    }

    [Fact]
    public void RemoveDiscount_restores_the_totals()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(4m), "WELCOME10"));

        order.RemoveDiscount(DiscountSource.Coupon, "welcome10");

        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), Assert.Single(order.Items).DiscountAmount);
        Assert.Equal(Given.Eur(12m), order.Total);
    }

    [Fact]
    public void RemoveDiscount_rejects_a_discount_that_is_not_applied()
    {
        var order = Given.DraftOrder();
        order.ApplyDiscount(Given.Discount(Given.Eur(4m), "WELCOME10", DiscountSource.Coupon));

        DomainAssert.Throws(OrderErrors.DiscountNotFound, () => order.RemoveDiscount(DiscountSource.Promotion, "WELCOME10"));
        DomainAssert.Throws(OrderErrors.DiscountNotFound, () => order.RemoveDiscount(DiscountSource.Coupon, "OTHER"));
    }

    [Fact]
    public void Totals_are_identical_whatever_the_sequence_of_mutations_that_led_to_the_same_content()
    {
        var burger = Given.Product(Given.Eur(9.99m), Given.Vat10);
        var soda = Given.Product(Given.Eur(2.35m), Given.Vat20);

        var direct = Given.DraftOrder();
        direct.AddItem(burger, Given.Qty(3));
        direct.AddItem(soda, Given.Qty(2));
        direct.ApplyDiscount(Given.Discount(Given.Eur(5m)));

        var detoured = Given.DraftOrder();
        detoured.ApplyDiscount(Given.Discount(Given.Eur(5m)));
        var burgerLine = detoured.AddItem(burger, Given.Qty(1));
        var removed = detoured.AddItem(soda, Given.Qty(7));
        detoured.AddItem(soda, Given.Qty(2));
        detoured.RemoveItem(removed);
        detoured.ChangeItemQuantity(burgerLine, Given.Qty(3));

        Assert.Equal(direct.SubTotal, detoured.SubTotal);
        Assert.Equal(direct.DiscountTotal, detoured.DiscountTotal);
        Assert.Equal(direct.TaxTotal, detoured.TaxTotal);
        Assert.Equal(direct.Total, detoured.Total);
    }

    [Theory]
    [InlineData(PriceTaxMode.TaxExclusive)]
    [InlineData(PriceTaxMode.TaxInclusive)]
    public void Parts_always_sum_exactly_to_the_totals_over_many_awkward_orders(PriceTaxMode mode)
    {
        var random = new Random(20261006);
        decimal[] rates = [0m, 0.055m, 0.10m, 0.18m, 0.20m, 0.09975m];
        Currency[] currencies = [Currency.EUR, Currency.XOF, Currency.FromCode("KWD")];

        for (var scenario = 0; scenario < 500; scenario++)
        {
            var currency = currencies[scenario % currencies.Length];
            var minorUnit = currency.DecimalPlaces switch { 0 => 1m, 2 => 0.01m, _ => 0.001m };
            var order = Given.DraftOrder(currency, mode: mode);
            var lineCount = random.Next(1, 9);
            for (var line = 0; line < lineCount; line++)
            {
                var price = Money.Of(random.Next(0, 5000) * minorUnit, currency);
                var tax = TaxSnapshot.Create("T", "Tax", rates[random.Next(rates.Length)]);
                order.AddItem(Given.Product(price, tax), Given.Qty(random.Next(1, 8)));
            }

            var discountCount = random.Next(0, 3);
            for (var discount = 0; discount < discountCount; discount++)
            {
                var amount = Money.Of(random.Next(1, 20000) * minorUnit, currency);
                order.ApplyDiscount(Given.Discount(amount, $"D{discount}"));
            }

            AssertConsistent(order);
        }
    }

    private static void AssertConsistent(Order order)
    {
        var zero = Money.Zero(order.Currency);
        var inclusive = order.PriceTaxMode == PriceTaxMode.TaxInclusive;
        var halfUnit = order.Currency.DecimalPlaces switch { 0 => 0.5m, 2 => 0.005m, _ => 0.0005m };
        Money Sum(Func<OrderItem, Money> part) => order.Items.Aggregate(zero, (sum, item) => sum + part(item));
        var decided = order.Discounts.Aggregate(zero, (sum, discount) => sum + discount.Amount);

        Assert.Equal(Sum(item => item.LineTotal), order.SubTotal);
        Assert.Equal(Money.Min(decided, order.SubTotal), order.DiscountTotal);
        Assert.Equal(Sum(item => item.DiscountAmount), order.DiscountTotal);
        Assert.Equal(Sum(item => item.TaxAmount), order.TaxTotal);
        Assert.Equal(Sum(item => item.NetAmount), order.NetTotal);
        Assert.Equal(Sum(item => item.TotalWithTax), order.Total);
        Assert.Equal(order.NetTotal + order.TaxTotal, order.Total);
        Assert.Equal(
            inclusive ? order.SubTotal - order.DiscountTotal : order.SubTotal - order.DiscountTotal + order.TaxTotal,
            order.Total);
        Assert.Equal(order.SubTotal - order.DiscountTotal, inclusive ? order.Total : order.NetTotal);
        Assert.False(order.Total.IsNegative);
        Assert.False(order.NetTotal.IsNegative);
        Assert.All(order.Items, item =>
        {
            Assert.False(item.DiscountAmount.IsNegative);
            Assert.True(item.DiscountAmount <= item.LineTotal);
            Assert.False(item.TaxAmount.IsNegative);
            Assert.False(item.NetAmount.IsNegative);
            Assert.Equal(item.TotalWithTax, item.NetAmount + item.TaxAmount);
            Assert.Equal(decimal.Round(item.TaxAmount.Amount, order.Currency.DecimalPlaces), item.TaxAmount.Amount);
            Assert.Equal(decimal.Round(item.NetAmount.Amount, order.Currency.DecimalPlaces), item.NetAmount.Amount);
            if (inclusive)
            {
                // Le client paie le prix carte remisé, à l'unité près ; la taxe est à moins d'une demi-unité de
                // la taxe incluse exacte brut x taux / (1 + taux).
                Assert.Equal(item.TaxableAmount, item.TotalWithTax);
                Assert.Equal(item.Tax.TaxIncludedIn(item.TaxableAmount), item.TaxAmount);
                var exactTax = item.TaxableAmount.Amount * item.Tax.Rate / (1m + item.Tax.Rate);
                Assert.True(Math.Abs(item.TaxAmount.Amount - exactTax) <= halfUnit);
            }
            else
            {
                Assert.Equal(item.TaxableAmount, item.NetAmount);
                Assert.Equal(item.Tax.TaxOn(item.TaxableAmount), item.TaxAmount);
                Assert.True(Math.Abs(item.TaxAmount.Amount - (item.TaxableAmount.Amount * item.Tax.Rate)) <= halfUnit);
            }
        });
    }

    [Fact]
    public void Tax_exclusive_totals_expose_the_net_total_before_tax()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10.00m), Given.Vat20), Given.Qty(2));
        order.AddItem(Given.Product(Given.Eur(3.50m), Given.Vat10), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(3.50m)));

        Assert.Equal(Given.Eur(20.00m), order.NetTotal);
        Assert.Equal(order.SubTotal - order.DiscountTotal, order.NetTotal);
        Assert.Equal(order.Total, order.NetTotal + order.TaxTotal);
        Assert.All(order.Items, item => Assert.Equal(item.TaxableAmount, item.NetAmount));
    }

    [Fact]
    public void Inclusive_totals_extract_the_tax_contained_in_tax_inclusive_prices()
    {
        var order = Given.InclusiveOrder();

        order.AddItem(Given.Product(Given.Eur(10.00m), Given.Vat20), Given.Qty(2));
        order.AddItem(Given.Product(Given.Eur(3.50m), Given.Vat10), Given.Qty(1));

        // 20.00 - 20.00 / 1.2 = 3.3333 -> 3.33 (net 16.67) ; 3.50 - 3.50 / 1.1 = 0.3181 -> 0.32 (net 3.18).
        var items = order.Items.ToArray();
        Assert.Equal([Given.Eur(3.33m), Given.Eur(0.32m)], items.Select(item => item.TaxAmount).ToArray());
        Assert.Equal([Given.Eur(16.67m), Given.Eur(3.18m)], items.Select(item => item.NetAmount).ToArray());
        Assert.Equal([Given.Eur(20.00m), Given.Eur(3.50m)], items.Select(item => item.TotalWithTax).ToArray());
        Assert.Equal(Given.Eur(23.50m), order.SubTotal);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(3.65m), order.TaxTotal);
        Assert.Equal(Given.Eur(23.50m), order.Total);
        Assert.Equal(Given.Eur(19.85m), order.NetTotal);
    }

    [Fact]
    public void Inclusive_totals_with_modifiers_mixed_rates_and_a_discount_match_the_documented_formula()
    {
        var order = Given.InclusiveOrder();
        var burger = Given.Product(
            Given.Eur(9.99m), Given.Vat10, "Burger", null,
            Given.Modifier("Cheese", Given.Eur(1.50m)),
            Given.Modifier("No bun", Given.Eur(-0.49m)));
        order.AddItem(burger, Given.Qty(3));
        order.AddItem(Given.Product(Given.Eur(2.35m), Given.Vat20, "Soda"), Given.Qty(2));

        order.ApplyDiscount(Given.Discount(Given.Eur(5.00m)));

        // Même répartition qu'en mode hors taxes : 4.38 / 0.62.
        // 28.62 - 28.62 / 1.1 = 2.6018 -> 2.60 (net 26.02) ; 4.08 - 4.08 / 1.2 = 0.68 (net 3.40).
        var items = order.Items.ToArray();
        Assert.Equal(Given.Eur(37.70m), order.SubTotal);
        Assert.Equal(Given.Eur(5.00m), order.DiscountTotal);
        Assert.Equal(Given.Eur(4.38m), items[0].DiscountAmount);
        Assert.Equal(Given.Eur(0.62m), items[1].DiscountAmount);
        Assert.Equal(Given.Eur(28.62m), items[0].TotalWithTax);
        Assert.Equal(Given.Eur(4.08m), items[1].TotalWithTax);
        Assert.Equal(Given.Eur(2.60m), items[0].TaxAmount);
        Assert.Equal(Given.Eur(0.68m), items[1].TaxAmount);
        Assert.Equal(Given.Eur(26.02m), items[0].NetAmount);
        Assert.Equal(Given.Eur(3.40m), items[1].NetAmount);
        Assert.Equal(Given.Eur(3.28m), order.TaxTotal);
        Assert.Equal(Given.Eur(32.70m), order.Total);
        Assert.Equal(Given.Eur(29.42m), order.NetTotal);
    }

    [Fact]
    public void Inclusive_totals_in_a_zero_decimal_currency_stay_whole_and_lose_no_unit()
    {
        var order = Given.InclusiveOrder(Currency.XOF);
        order.AddItem(Given.Product(Given.Xof(1000m), Given.Vat18), Given.Qty(1));
        order.AddItem(Given.Product(Given.Xof(500m), Given.Vat18), Given.Qty(1));
        order.AddItem(Given.Product(Given.Xof(333m), Given.Vat18), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Xof(100m)));

        Assert.Equal(
            [Given.Xof(55m), Given.Xof(27m), Given.Xof(18m)],
            order.Items.Select(item => item.DiscountAmount).ToArray());
        // 945 - 945 / 1.18 = 144.15 ; 473 - 473 / 1.18 = 72.15 ; 315 - 315 / 1.18 = 48.05.
        Assert.Equal(
            [Given.Xof(144m), Given.Xof(72m), Given.Xof(48m)],
            order.Items.Select(item => item.TaxAmount).ToArray());
        Assert.Equal(
            [Given.Xof(801m), Given.Xof(401m), Given.Xof(267m)],
            order.Items.Select(item => item.NetAmount).ToArray());
        Assert.Equal(Given.Xof(1833m), order.SubTotal);
        Assert.Equal(Given.Xof(100m), order.DiscountTotal);
        Assert.Equal(Given.Xof(264m), order.TaxTotal);
        Assert.Equal(Given.Xof(1733m), order.Total);
        Assert.Equal(Given.Xof(1469m), order.NetTotal);
    }

    [Fact]
    public void Inclusive_totals_in_a_three_decimal_currency_keep_three_decimals()
    {
        var kwd = Currency.FromCode("KWD");
        var order = Given.InclusiveOrder(kwd);

        order.AddItem(Given.Product(Money.Of(0.333m, kwd), TaxSnapshot.Create("VAT5", "VAT 5 %", 0.05m)), Given.Qty(3));

        // 0.999 - 0.999 / 1.05 = 0.04757 -> 0.048 (net 0.951).
        Assert.Equal(Money.Of(0.999m, kwd), order.SubTotal);
        Assert.Equal(Money.Of(0.048m, kwd), order.TaxTotal);
        Assert.Equal(Money.Of(0.999m, kwd), order.Total);
        Assert.Equal(Money.Of(0.951m, kwd), order.NetTotal);
    }

    [Fact]
    public void Inclusive_prices_at_rate_zero_contain_no_tax()
    {
        var order = Given.InclusiveOrder();

        order.AddItem(Given.Product(Given.Eur(12.34m), Given.NoTax), Given.Qty(2));

        Assert.Equal(Given.Eur(0m), order.TaxTotal);
        Assert.Equal(Given.Eur(24.68m), order.Total);
        Assert.Equal(Given.Eur(24.68m), order.NetTotal);
    }

    [Theory]
    [InlineData(10.00, 0.09975, 0.91, 9.09)] // 10 - 10 / 1.09975 = 0.90702
    [InlineData(12.34, 0.055, 0.64, 11.70)] // 12.34 - 12.34 / 1.055 = 0.64332
    [InlineData(0.01, 0.20, 0.00, 0.01)] // 0.01 - 0.01 / 1.2 = 0.00167
    [InlineData(99.99, 0.021, 2.06, 97.93)] // 99.99 - 99.99 / 1.021 = 2.05661
    public void Inclusive_tax_with_an_awkward_rate_keeps_net_plus_tax_equal_to_the_price(
        double price, double rate, double expectedTax, double expectedNet)
    {
        var order = Given.InclusiveOrder();

        order.AddItem(Given.Product(Given.Eur((decimal)price), TaxSnapshot.Create("T", "Tax", (decimal)rate)), Given.Qty(1));

        var item = Assert.Single(order.Items);
        Assert.Equal(Given.Eur((decimal)expectedTax), item.TaxAmount);
        Assert.Equal(Given.Eur((decimal)expectedNet), item.NetAmount);
        Assert.Equal(Given.Eur((decimal)price), item.NetAmount + item.TaxAmount);
        Assert.Equal(Given.Eur((decimal)price), order.Total);
    }

    [Fact]
    public void Inclusive_line_tax_is_rounded_half_away_from_zero()
    {
        var order = Given.InclusiveOrder();

        // Taux 100 % : la taxe vaut exactement la moitié de 0.05, soit 0.025 -> 0.03 (l'arrondi bancaire donnerait 0.02).
        order.AddItem(Given.Product(Given.Eur(0.05m), TaxSnapshot.Create("FULL", "100 %", 1m)), Given.Qty(1));

        Assert.Equal(Given.Eur(0.03m), order.TaxTotal);
        Assert.Equal(Given.Eur(0.02m), order.NetTotal);
        Assert.Equal(Given.Eur(0.05m), order.Total);
    }

    [Fact]
    public void Inclusive_tax_is_rounded_per_line_and_the_total_is_the_sum_of_line_taxes()
    {
        var order = Given.InclusiveOrder();
        for (var i = 0; i < 3; i++)
        {
            order.AddItem(Given.Product(Given.Eur(0.07m), Given.Vat20), Given.Qty(1));
        }

        // 0.07 - 0.07 / 1.2 = 0.01167 -> 0.01 par ligne : 0.03 (0.21 extrait globalement donnerait 0.035 -> 0.04).
        Assert.Equal(Given.Eur(0.03m), order.TaxTotal);
        Assert.Equal(Given.Eur(0.21m), order.Total);
        Assert.Equal(Given.Eur(0.18m), order.NetTotal);
    }

    [Fact]
    public void Inclusive_discount_is_a_tax_inclusive_amount_and_tax_is_extracted_from_the_discounted_price()
    {
        var order = Given.InclusiveOrder();
        order.AddItem(Given.Product(Given.Eur(100m), Given.Vat20), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(25m)));

        // 75.00 payés : 75 - 75 / 1.2 = 12.50 de taxe, 62.50 net.
        Assert.Equal(Given.Eur(25m), order.DiscountTotal);
        Assert.Equal(Given.Eur(75m), order.Total);
        Assert.Equal(Given.Eur(12.50m), order.TaxTotal);
        Assert.Equal(Given.Eur(62.50m), order.NetTotal);
    }

    [Fact]
    public void Inclusive_discount_larger_than_the_subtotal_is_capped_and_leaves_no_tax()
    {
        var order = Given.InclusiveOrder();
        order.AddItem(Given.Product(Given.Eur(0.01m), Given.Vat20), Given.Qty(1));
        order.AddItem(Given.Product(Given.Eur(29.99m), Given.Vat10), Given.Qty(1));

        order.ApplyDiscount(Given.Discount(Given.Eur(50m)));

        Assert.Equal(Given.Eur(30m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), order.TaxTotal);
        Assert.Equal(Given.Eur(0m), order.NetTotal);
        Assert.Equal(Given.Eur(0m), order.Total);
    }

    [Fact]
    public void Same_basket_costs_the_menu_price_in_inclusive_mode_and_more_in_exclusive_mode()
    {
        var product = Given.Product(Given.Eur(12m), Given.Vat20);
        var inclusive = Given.InclusiveOrder();
        var exclusive = Given.DraftOrder();

        inclusive.AddItem(product, Given.Qty(1));
        exclusive.AddItem(product, Given.Qty(1));

        Assert.Equal((Given.Eur(12m), Given.Eur(2m), Given.Eur(10m)), (inclusive.Total, inclusive.TaxTotal, inclusive.NetTotal));
        Assert.Equal((Given.Eur(14.40m), Given.Eur(2.40m), Given.Eur(12m)), (exclusive.Total, exclusive.TaxTotal, exclusive.NetTotal));
    }

    [Fact]
    public void ClearDiscounts_removes_every_discount_and_restores_the_totals()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(2m), "PROMO-LUNCH", DiscountSource.Promotion));
        order.ApplyDiscount(Given.Discount(Given.Eur(3m), "WELCOME10", DiscountSource.Coupon));
        order.ClearDomainEvents();

        order.ClearDiscounts();

        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Eur(0m), order.DiscountTotal);
        Assert.Equal(Given.Eur(0m), Assert.Single(order.Items).DiscountAmount);
        Assert.Equal(Given.Eur(2m), order.TaxTotal);
        Assert.Equal(Given.Eur(12m), order.Total);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void ClearDiscounts_does_nothing_when_there_is_no_discount()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.Vat20), Given.Qty(1));
        order.ClearDomainEvents();

        order.ClearDiscounts();

        Assert.Empty(order.Discounts);
        Assert.Equal(Given.Eur(12m), order.Total);
        DomainAssert.NoEvents(order);
    }

    [Fact]
    public void ClearDiscounts_lets_the_same_origin_be_applied_again_with_a_fresh_amount()
    {
        var order = Given.DraftOrder();
        order.AddItem(Given.Product(Given.Eur(10m), Given.NoTax), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(1m), "TENPERCENT", DiscountSource.Promotion));
        order.AddItem(Given.Product(Given.Eur(30m), Given.NoTax), Given.Qty(1));

        order.ClearDiscounts();
        order.ApplyDiscount(Given.Discount(Given.Eur(4m), "TENPERCENT", DiscountSource.Promotion));

        Assert.Equal(Given.Eur(4m), Assert.Single(order.Discounts).Amount);
        Assert.Equal(Given.Eur(36m), order.Total);
    }

    [Fact]
    public void Changing_lines_does_not_clear_discounts_by_itself()
    {
        var order = Given.DraftOrder();
        var line = order.AddItem(Given.Product(Given.Eur(10m), Given.NoTax), Given.Qty(1));
        order.ApplyDiscount(Given.Discount(Given.Eur(1m)));

        order.AddItem(Given.Product(Given.Eur(30m), Given.NoTax), Given.Qty(1));
        order.RemoveItem(line);

        Assert.Single(order.Discounts);
        Assert.Equal(Given.Eur(1m), order.DiscountTotal);
    }

    private static ProductSnapshot ProductWithId(string guid, Money unitPrice, TaxSnapshot tax) =>
        ProductSnapshot.Create(Given.Tenant, ProductId.From(Guid.Parse(guid)), "P", unitPrice, tax);

    [Fact]
    public void Leftover_unit_does_not_depend_on_the_order_in_which_tied_lines_were_added()
    {
        // 0.05 à 10 % et 0.05 non taxé, 0.01 de remise : un demi-centime chacun, un centime à attribuer. Sur la ligne
        // taxée, la taxe passe de 0.005 -> 0.01 à 0.004 -> 0.00 : qui reçoit le centime change le total (0.09 ou 0.10).
        var taxed = ProductWithId("00000000-0000-0000-0000-000000000001", Given.Eur(0.05m), Given.Vat10);
        var untaxed = ProductWithId("00000000-0000-0000-0000-000000000002", Given.Eur(0.05m), Given.NoTax);
        var taxedFirst = Given.DraftOrder();
        taxedFirst.AddItem(taxed, Given.Qty(1));
        taxedFirst.AddItem(untaxed, Given.Qty(1));
        var taxedLast = Given.DraftOrder();
        taxedLast.AddItem(untaxed, Given.Qty(1));
        taxedLast.AddItem(taxed, Given.Qty(1));

        taxedFirst.ApplyDiscount(Given.Discount(Given.Eur(0.01m)));
        taxedLast.ApplyDiscount(Given.Discount(Given.Eur(0.01m)));

        // L'égalité est tranchée en faveur du plus petit id de produit, où que soit sa ligne.
        Assert.All(new[] { taxedFirst, taxedLast }, order =>
        {
            Assert.Equal(Given.Eur(0.01m), order.Items.Single(item => item.ProductId == taxed.ProductId).DiscountAmount);
            Assert.Equal(Given.Eur(0m), order.Items.Single(item => item.ProductId == untaxed.ProductId).DiscountAmount);
            Assert.Equal(Given.Eur(0m), order.TaxTotal);
            Assert.Equal(Given.Eur(0.09m), order.Total);
        });
    }

    [Fact]
    public void Leftover_unit_goes_to_the_largest_line_when_remainders_tie()
    {
        // 0.02 sur 0.01 et 0.03 : parties entières 0 et 1, restes égaux à 2/4 ; la plus grande ligne prend le centime.
        var small = Given.Product(Given.Eur(0.01m), Given.NoTax);
        var large = Given.Product(Given.Eur(0.03m), Given.NoTax);
        var smallFirst = Given.DraftOrder();
        smallFirst.AddItem(small, Given.Qty(1));
        smallFirst.AddItem(large, Given.Qty(1));
        var largeFirst = Given.DraftOrder();
        largeFirst.AddItem(large, Given.Qty(1));
        largeFirst.AddItem(small, Given.Qty(1));

        smallFirst.ApplyDiscount(Given.Discount(Given.Eur(0.02m)));
        largeFirst.ApplyDiscount(Given.Discount(Given.Eur(0.02m)));

        Assert.Equal([Given.Eur(0m), Given.Eur(0.02m)], smallFirst.Items.Select(item => item.DiscountAmount).ToArray());
        Assert.Equal([Given.Eur(0.02m), Given.Eur(0m)], largeFirst.Items.Select(item => item.DiscountAmount).ToArray());
    }

    [Theory]
    [InlineData(PriceTaxMode.TaxExclusive)]
    [InlineData(PriceTaxMode.TaxInclusive)]
    public void Totals_and_line_amounts_are_the_same_whatever_the_order_in_which_the_lines_were_added(PriceTaxMode mode)
    {
        var random = new Random(20261007);
        decimal[] rates = [0m, 0.055m, 0.10m, 0.20m];

        for (var scenario = 0; scenario < 300; scenario++)
        {
            // Petits prix répétés et remises minimes : les restes sont sans cesse à égalité.
            var lines = Enumerable.Range(0, random.Next(2, 7))
                .Select(_ => (
                    Product: Given.Product(Given.Eur(random.Next(1, 4) * 0.05m), TaxSnapshot.Create("T", "Tax", rates[random.Next(rates.Length)])),
                    Quantity: Given.Qty(random.Next(1, 3))))
                .ToArray();
            var discount = Given.Eur(random.Next(1, 6) * 0.01m);
            var shuffled = lines.OrderBy(_ => random.Next()).ToArray();

            var first = OrderOf(mode, lines, discount);
            var second = OrderOf(mode, shuffled, discount);

            Assert.Equal(
                (first.SubTotal, first.DiscountTotal, first.TaxTotal, first.Total),
                (second.SubTotal, second.DiscountTotal, second.TaxTotal, second.Total));
            Assert.Equal(AmountsByProduct(first), AmountsByProduct(second));
        }
    }

    private static Order OrderOf(PriceTaxMode mode, (ProductSnapshot Product, Quantity Quantity)[] lines, Money discount)
    {
        var order = Given.DraftOrder(mode: mode);
        foreach (var (product, quantity) in lines)
        {
            order.AddItem(product, quantity);
        }

        order.ApplyDiscount(Given.Discount(discount));
        return order;
    }

    private static (ProductId, Money, Money, Money)[] AmountsByProduct(Order order) =>
        order.Items
            .Select(item => (item.ProductId, item.DiscountAmount, item.NetAmount, item.TaxAmount))
            .OrderBy(line => line.ProductId.Value)
            .ToArray();
}

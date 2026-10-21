namespace Orders.Domain;

public class OrderNumberTests
{
    [Theory]
    [InlineData("A-0042", "A-0042")]
    [InlineData("  a-0042 ", "A-0042")]
    [InlineData("20260115-017", "20260115-017")]
    [InlineData("42", "42")]
    [InlineData("web-2026-000123", "WEB-2026-000123")]
    public void Create_normalizes_a_valid_number(string input, string expected) =>
        Assert.Equal(expected, OrderNumber.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_an_empty_number(string? input) =>
        DomainAssert.Throws(OrderErrors.NumberEmpty, () => OrderNumber.Create(input));

    [Fact]
    public void Create_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(OrderNumber.MaxLength, OrderNumber.Create(new string('7', OrderNumber.MaxLength)).Value.Length);
        DomainAssert.Throws(OrderErrors.NumberTooLong, () => OrderNumber.Create(new string('7', OrderNumber.MaxLength + 1)));
    }

    [Theory]
    [InlineData("-A1")]
    [InlineData("A1-")]
    [InlineData("A--1")]
    [InlineData("A 1")]
    [InlineData("A_1")]
    [InlineData("A#1")]
    [InlineData("É-1")]
    [InlineData("-")]
    public void Create_rejects_an_invalid_format(string input) =>
        DomainAssert.Throws(OrderErrors.NumberInvalidFormat, () => OrderNumber.Create(input));

    [Fact]
    public void Numbers_are_equal_by_normalized_value()
    {
        Assert.Equal(OrderNumber.Create("a-1"), OrderNumber.Create("A-1"));
        Assert.NotEqual(OrderNumber.Create("A-1"), OrderNumber.Create("A-2"));
        Assert.Equal("A-1", OrderNumber.Create("a-1").ToString());
    }
}

public class QuantityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(Quantity.MaxValue)]
    public void Create_accepts_a_positive_bounded_quantity(int value) => Assert.Equal(value, Quantity.Create(value).Value);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_rejects_zero_or_negative(int value) =>
        DomainAssert.Throws(OrderErrors.QuantityNotPositive, () => Quantity.Create(value));

    [Fact]
    public void Create_rejects_more_than_the_maximum() =>
        DomainAssert.Throws(OrderErrors.QuantityTooLarge, () => Quantity.Create(Quantity.MaxValue + 1));

    [Fact]
    public void Quantities_are_equal_by_value()
    {
        Assert.Equal(Quantity.Create(3), Quantity.Create(3));
        Assert.NotEqual(Quantity.Create(3), Quantity.Create(4));
    }
}

public class TaxSnapshotTests
{
    [Fact]
    public void Create_keeps_a_normalized_snapshot()
    {
        var tax = TaxSnapshot.Create(" vat20 ", " TVA 20 % ", 0.20m);

        Assert.Equal("VAT20", tax.CategoryCode);
        Assert.Equal("TVA 20 %", tax.CategoryName);
        Assert.Equal(0.20m, tax.Rate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.055)]
    [InlineData(0.09975)]
    [InlineData(1)]
    public void Create_accepts_a_rate_between_zero_and_one(double rate) =>
        Assert.Equal((decimal)rate, TaxSnapshot.Create("T", "Tax", (decimal)rate).Rate);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(20)]
    public void Create_rejects_a_rate_outside_zero_to_one(double rate) =>
        DomainAssert.Throws(OrderErrors.TaxRateOutOfRange, () => TaxSnapshot.Create("T", "Tax", (decimal)rate));

    [Fact]
    public void Create_rejects_a_rate_with_more_than_six_decimals() =>
        DomainAssert.Throws(OrderErrors.TaxRateTooPrecise, () => TaxSnapshot.Create("T", "Tax", 0.1234567m));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void Create_rejects_a_missing_or_too_long_code(string? code) =>
        DomainAssert.Throws(OrderErrors.TaxCategoryCodeInvalid, () => TaxSnapshot.Create(code, "Tax", 0.2m));

    [Fact]
    public void Create_rejects_a_missing_or_too_long_name()
    {
        DomainAssert.Throws(OrderErrors.TaxCategoryNameInvalid, () => TaxSnapshot.Create("T", " ", 0.2m));
        DomainAssert.Throws(
            OrderErrors.TaxCategoryNameInvalid,
            () => TaxSnapshot.Create("T", new string('n', TaxSnapshot.MaxCategoryNameLength + 1), 0.2m));
    }

    [Fact]
    public void TaxOn_rounds_half_away_from_zero_to_the_currency_precision()
    {
        Assert.Equal(Given.Eur(0.03m), Given.Vat10.TaxOn(Given.Eur(0.25m)));
        Assert.Equal(Given.Eur(0.01m), Given.Vat20.TaxOn(Given.Eur(0.07m)));
        Assert.Equal(Given.Xof(57m), Given.Vat18.TaxOn(Given.Xof(315m)));
        Assert.Equal(Given.Eur(0m), Given.NoTax.TaxOn(Given.Eur(99.99m)));
    }

    [Fact]
    public void TaxIncludedIn_extracts_the_tax_contained_in_a_gross_amount()
    {
        // 12.00 - 12.00 / 1.2 = 2.00 ; 3.50 - 3.50 / 1.1 = 0.3181... ; un taux 0 ne contient aucune taxe.
        Assert.Equal(Given.Eur(2.00m), Given.Vat20.TaxIncludedIn(Given.Eur(12.00m)));
        Assert.Equal(Given.Eur(0.32m), Given.Vat10.TaxIncludedIn(Given.Eur(3.50m)));
        Assert.Equal(Given.Eur(0m), Given.NoTax.TaxIncludedIn(Given.Eur(12.00m)));
        Assert.Equal(Given.Xof(153m), Given.Vat18.TaxIncludedIn(Given.Xof(1000m)));
    }

    [Fact]
    public void TaxIncludedIn_rounds_half_away_from_zero_to_the_currency_precision()
    {
        var full = TaxSnapshot.Create("FULL", "100 %", 1m);

        // Exactement la moitié du brut : 0.005 -> 0.01 et 0.025 -> 0.03 (l'arrondi bancaire donnerait 0.00 et 0.02).
        Assert.Equal(Given.Eur(0.01m), full.TaxIncludedIn(Given.Eur(0.01m)));
        Assert.Equal(Given.Eur(0.03m), full.TaxIncludedIn(Given.Eur(0.05m)));
    }

    [Fact]
    public void Snapshots_are_equal_by_value()
    {
        Assert.Equal(TaxSnapshot.Create("VAT20", "TVA 20 %", 0.2m), TaxSnapshot.Create("vat20", "TVA 20 %", 0.20m));
        Assert.NotEqual(Given.Vat20, Given.Vat10);
    }
}

public class OrderItemModifierTests
{
    [Fact]
    public void Create_keeps_the_snapshot()
    {
        var id = ModifierId.New();

        var modifier = OrderItemModifier.Create(id, "  Extra cheese ", Given.Eur(1.50m));

        Assert.Equal(id, modifier.ModifierId);
        Assert.Equal("Extra cheese", modifier.Name);
        Assert.Equal(Given.Eur(1.50m), modifier.PriceDelta);
    }

    [Fact]
    public void Create_accepts_a_zero_or_negative_delta()
    {
        Assert.True(OrderItemModifier.Create(ModifierId.New(), "No onion", Given.Eur(0m)).PriceDelta.IsZero);
        Assert.True(OrderItemModifier.Create(ModifierId.New(), "No cheese", Given.Eur(-0.50m)).PriceDelta.IsNegative);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_a_missing_name(string? name) =>
        DomainAssert.Throws(OrderErrors.ModifierNameInvalid, () => OrderItemModifier.Create(ModifierId.New(), name, Given.Eur(1m)));

    [Fact]
    public void Create_rejects_a_too_long_name() =>
        DomainAssert.Throws(
            OrderErrors.ModifierNameInvalid,
            () => OrderItemModifier.Create(ModifierId.New(), new string('x', OrderItemModifier.MaxNameLength + 1), Given.Eur(1m)));

    [Fact]
    public void Create_accepts_a_name_as_long_as_the_catalogue_allows()
    {
        var name = new string('x', 120);

        Assert.Equal(name, OrderItemModifier.Create(ModifierId.New(), name, Given.Eur(1m)).Name);
        Assert.Equal(120, OrderItemModifier.MaxNameLength);
    }

    [Fact]
    public void Create_rejects_an_empty_modifier_id() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderItemModifier.Create(default, "Cheese", Given.Eur(1m)));

    [Fact]
    public void Modifiers_are_equal_when_id_name_and_delta_match()
    {
        var id = ModifierId.New();
        var modifier = OrderItemModifier.Create(id, "Extra cheese", Given.Eur(1.50m));

        Assert.Equal(modifier, OrderItemModifier.Create(id, "Extra cheese", Given.Eur(1.5m)));
        Assert.Equal(modifier.GetHashCode(), OrderItemModifier.Create(id, "Extra cheese", Given.Eur(1.5m)).GetHashCode());
        Assert.True(modifier == OrderItemModifier.Create(id, "Extra cheese", Given.Eur(1.50m)));
    }

    [Fact]
    public void Modifiers_differ_when_any_component_differs()
    {
        var id = ModifierId.New();
        var modifier = OrderItemModifier.Create(id, "Extra cheese", Given.Eur(1.50m));

        Assert.NotEqual(modifier, OrderItemModifier.Create(ModifierId.New(), "Extra cheese", Given.Eur(1.50m)));
        Assert.NotEqual(modifier, OrderItemModifier.Create(id, "Extra bacon", Given.Eur(1.50m)));
        Assert.NotEqual(modifier, OrderItemModifier.Create(id, "Extra cheese", Given.Eur(2.00m)));
        Assert.NotEqual(modifier, OrderItemModifier.Create(id, "Extra cheese", Money.Of(1.50m, Currency.USD)));
    }
}

public class ProductSnapshotTests
{
    [Fact]
    public void Create_keeps_every_fact_of_the_product()
    {
        var productId = ProductId.New();
        var cheese = Given.Modifier("Cheese", Given.Eur(1m));

        var snapshot = ProductSnapshot.Create(Given.Tenant, productId, " Burger ", Given.Eur(9.99m), Given.Vat10, [cheese]);

        Assert.Equal(Given.Tenant, snapshot.TenantId);
        Assert.Equal(productId, snapshot.ProductId);
        Assert.Equal("Burger", snapshot.Name);
        Assert.Equal(Given.Eur(9.99m), snapshot.UnitPrice);
        Assert.Equal(Given.Vat10, snapshot.Tax);
        Assert.Equal(cheese, Assert.Single(snapshot.Modifiers));
    }

    [Fact]
    public void Create_accepts_a_free_product() =>
        Assert.True(Given.Product(Given.Eur(0m)).UnitPrice.IsZero);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_rejects_a_missing_name(string? name) =>
        DomainAssert.Throws(
            OrderErrors.ProductNameInvalid,
            () => ProductSnapshot.Create(Given.Tenant, ProductId.New(), name, Given.Eur(1m), Given.Vat20));

    [Fact]
    public void Create_rejects_a_too_long_name() =>
        DomainAssert.Throws(
            OrderErrors.ProductNameInvalid,
            () => Given.Product(Given.Eur(1m), name: new string('p', ProductSnapshot.MaxNameLength + 1)));

    [Fact]
    public void Create_rejects_a_negative_unit_price() =>
        DomainAssert.Throws(OrderErrors.ProductUnitPriceNegative, () => Given.Product(Given.Eur(-0.01m)));

    [Fact]
    public void Create_requires_price_and_tax()
    {
        Assert.Throws<ArgumentNullException>(
            () => ProductSnapshot.Create(Given.Tenant, ProductId.New(), "Burger", null!, Given.Vat20));
        Assert.Throws<ArgumentNullException>(
            () => ProductSnapshot.Create(Given.Tenant, ProductId.New(), "Burger", Given.Eur(1m), null!));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_product_or_variant_id()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ProductSnapshot.Create(default, ProductId.New(), "Burger", Given.Eur(1m), Given.Vat20));
        DomainAssert.Throws(CommonErrors.EmptyId, () => ProductSnapshot.Create(Given.Tenant, default, "Burger", Given.Eur(1m), Given.Vat20));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Given.VariantProduct(Given.Eur(1m), default, "Large"));
    }

    [Fact]
    public void Create_rejects_a_null_modifier_as_a_programming_error() =>
        Assert.Throws<ArgumentNullException>(
            () => ProductSnapshot.Create(Given.Tenant, ProductId.New(), "Burger", Given.Eur(1m), Given.Vat20, [null!]));

    [Fact]
    public void Create_keeps_the_variant_with_its_trimmed_name()
    {
        var variant = VariantId.New();

        var snapshot = Given.VariantProduct(Given.Eur(14m), variant, "  Large ");

        Assert.Equal(variant, snapshot.VariantId);
        Assert.Equal("Large", snapshot.VariantName);
        Assert.Null(Given.Product(Given.Eur(1m)).VariantId);
        Assert.Null(Given.Product(Given.Eur(1m)).VariantName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_the_name_of_the_variant(string? variantName) =>
        DomainAssert.Throws(
            OrderErrors.ProductVariantNameRequired, () => Given.VariantProduct(Given.Eur(14m), VariantId.New(), variantName!));

    [Fact]
    public void Create_rejects_a_variant_name_without_a_variant() =>
        DomainAssert.Throws(
            OrderErrors.ProductVariantNameWithoutVariant,
            () => ProductSnapshot.Create(Given.Tenant, ProductId.New(), "Pizza", Given.Eur(14m), Given.Vat20, null, null, "Large"));

    [Fact]
    public void Create_accepts_a_variant_name_of_the_maximum_length_and_rejects_one_more()
    {
        var longest = new string('v', ProductSnapshot.MaxVariantNameLength);

        Assert.Equal(longest, Given.VariantProduct(Given.Eur(14m), VariantId.New(), longest).VariantName);
        DomainAssert.Throws(
            OrderErrors.ProductVariantNameTooLong, () => Given.VariantProduct(Given.Eur(14m), VariantId.New(), longest + "v"));
    }

    [Fact]
    public void Snapshots_of_two_variants_of_the_same_product_differ()
    {
        var productId = ProductId.New();
        var variant = VariantId.New();
        ProductSnapshot Build(VariantId id, string name) =>
            ProductSnapshot.Create(Given.Tenant, productId, "Pizza", Given.Eur(14m), Given.Vat20, null, id, name);

        Assert.Equal(Build(variant, "Large"), Build(variant, "Large"));
        Assert.NotEqual(Build(variant, "Large"), Build(VariantId.New(), "Large"));
    }

    [Fact]
    public void Snapshots_are_equal_by_value_including_modifiers()
    {
        var productId = ProductId.New();
        var cheese = Given.Modifier("Cheese", Given.Eur(1m));
        ProductSnapshot Build(params OrderItemModifier[] modifiers) =>
            ProductSnapshot.Create(Given.Tenant, productId, "Burger", Given.Eur(9.99m), Given.Vat10, modifiers);

        Assert.Equal(Build(cheese), Build(cheese));
        Assert.NotEqual(Build(cheese), Build());
    }
}

public class OrderDiscountTests
{
    [Fact]
    public void Create_keeps_amount_source_and_reference()
    {
        var discount = OrderDiscount.Create(Given.Eur(5m), DiscountSource.Coupon, " WELCOME10 ", " Bienvenue ");

        Assert.Equal(Given.Eur(5m), discount.Amount);
        Assert.Equal(DiscountSource.Coupon, discount.Source);
        Assert.Equal("WELCOME10", discount.Reference);
        Assert.Equal("Bienvenue", discount.Label);
    }

    [Fact]
    public void Create_treats_a_blank_label_as_absent() =>
        Assert.Null(OrderDiscount.Create(Given.Eur(5m), DiscountSource.Manual, "MGR-1", "  ").Label);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_zero_or_negative_amount(int amount) =>
        DomainAssert.Throws(
            OrderErrors.DiscountAmountNotPositive,
            () => OrderDiscount.Create(Given.Eur(amount), DiscountSource.Coupon, "C"));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_rejects_a_missing_reference(string? reference) =>
        DomainAssert.Throws(
            OrderErrors.DiscountReferenceInvalid,
            () => OrderDiscount.Create(Given.Eur(1m), DiscountSource.Coupon, reference));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Create_rejects_an_unknown_source(int source) =>
        DomainAssert.Throws(
            OrderErrors.DiscountSourceUnknown, () => OrderDiscount.Create(Given.Eur(1m), (DiscountSource)source, "C"));

    [Fact]
    public void Create_rejects_a_too_long_reference_or_label()
    {
        DomainAssert.Throws(
            OrderErrors.DiscountReferenceInvalid,
            () => OrderDiscount.Create(Given.Eur(1m), DiscountSource.Coupon, new string('r', OrderDiscount.MaxReferenceLength + 1)));
        DomainAssert.Throws(
            OrderErrors.DiscountLabelTooLong,
            () => OrderDiscount.Create(Given.Eur(1m), DiscountSource.Coupon, "C", new string('l', OrderDiscount.MaxLabelLength + 1)));
    }

    [Fact]
    public void Discounts_are_equal_by_value_with_a_case_insensitive_reference()
    {
        Assert.Equal(
            OrderDiscount.Create(Given.Eur(5m), DiscountSource.Coupon, "welcome10", "L"),
            OrderDiscount.Create(Given.Eur(5m), DiscountSource.Coupon, "WELCOME10", "L"));
        Assert.NotEqual(
            OrderDiscount.Create(Given.Eur(5m), DiscountSource.Coupon, "WELCOME10"),
            OrderDiscount.Create(Given.Eur(5m), DiscountSource.Promotion, "WELCOME10"));
    }
}

public class OrderSettlementTests
{
    [Fact]
    public void Create_keeps_tenant_order_and_paid_amount()
    {
        var tenant = TenantId.New();
        var orderId = OrderId.New();

        var settlement = OrderSettlement.Create(tenant, orderId, Given.Eur(12m));

        Assert.Equal(tenant, settlement.TenantId);
        Assert.Equal(orderId, settlement.OrderId);
        Assert.Equal(Given.Eur(12m), settlement.PaidAmount);
    }

    [Fact]
    public void Create_accepts_a_zero_paid_amount() =>
        Assert.True(OrderSettlement.Create(TenantId.New(), OrderId.New(), Given.Eur(0m)).PaidAmount.IsZero);

    [Fact]
    public void Create_rejects_a_negative_paid_amount() =>
        DomainAssert.Throws(
            OrderErrors.SettlementPaidAmountNegative,
            () => OrderSettlement.Create(TenantId.New(), OrderId.New(), Given.Eur(-0.01m)));

    [Fact]
    public void Create_requires_a_paid_amount() =>
        Assert.Throws<ArgumentNullException>(() => OrderSettlement.Create(TenantId.New(), OrderId.New(), null!));

    [Fact]
    public void Create_rejects_an_empty_tenant_or_order_id()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderSettlement.Create(default, OrderId.New(), Given.Eur(1m)));
        DomainAssert.Throws(CommonErrors.EmptyId, () => OrderSettlement.Create(TenantId.New(), default, Given.Eur(1m)));
    }

    [Fact]
    public void Settlements_are_equal_by_value()
    {
        var tenant = TenantId.New();
        var orderId = OrderId.New();

        Assert.Equal(OrderSettlement.Create(tenant, orderId, Given.Eur(5m)), OrderSettlement.Create(tenant, orderId, Given.Eur(5m)));
        Assert.NotEqual(OrderSettlement.Create(tenant, orderId, Given.Eur(5m)), OrderSettlement.Create(tenant, orderId, Given.Eur(6m)));
    }
}

namespace Payments.Domain;

public class ProviderCodeTests
{
    [Theory]
    [InlineData("stripe", "stripe")]
    [InlineData("  Stripe ", "stripe")]
    [InlineData("CINETPAY", "cinetpay")]
    [InlineData("orange_money-ci2", "orange_money-ci2")]
    [InlineData("ab", "ab")]
    public void Create_normalizes_a_valid_code(string input, string expected) =>
        Assert.Equal(expected, PaymentProviderCode.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("1stripe")]
    [InlineData("_stripe")]
    [InlineData("stripe pay")]
    [InlineData("stripe.com")]
    [InlineData("stripé")]
    public void Create_rejects_an_invalid_code(string? input) =>
        DomainAssert.Throws(PaymentErrors.ProviderCodeInvalid, () => PaymentProviderCode.Create(input));

    [Fact]
    public void Create_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(PaymentProviderCode.MaxLength, PaymentProviderCode.Create(new string('a', PaymentProviderCode.MaxLength)).Value.Length);
        DomainAssert.Throws(PaymentErrors.ProviderCodeInvalid, () => PaymentProviderCode.Create(new string('a', PaymentProviderCode.MaxLength + 1)));
    }

    [Fact]
    public void Codes_are_equal_by_normalized_value()
    {
        Assert.Equal(PaymentProviderCode.Create("Stripe"), PaymentProviderCode.Create("stripe"));
        Assert.NotEqual(PaymentProviderCode.Create("stripe"), PaymentProviderCode.Create("paypal"));
        Assert.Equal("stripe", PaymentProviderCode.Create("stripe").ToString());
    }
}

public class ProviderReferenceTests
{
    [Fact]
    public void Create_trims_and_keeps_the_case()
    {
        Assert.Equal("pi_3Nabc_XYZ", ProviderReference.Create("  pi_3Nabc_XYZ ").Value);
        Assert.NotEqual(ProviderReference.Create("pi_abc"), ProviderReference.Create("PI_ABC"));
        Assert.Equal(ProviderReference.Create("pi_abc"), ProviderReference.Create("pi_abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pi 1")]
    [InlineData("pi\t1")]
    [InlineData("pi\u00011")]
    public void Create_rejects_an_empty_or_malformed_reference(string? input) =>
        DomainAssert.Throws(PaymentErrors.ProviderReferenceInvalid, () => ProviderReference.Create(input));

    [Fact]
    public void Create_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(ProviderReference.MaxLength, ProviderReference.Create(new string('r', ProviderReference.MaxLength)).Value.Length);
        DomainAssert.Throws(
            PaymentErrors.ProviderReferenceInvalid,
            () => ProviderReference.Create(new string('r', ProviderReference.MaxLength + 1)));
    }
}

public class PaymentIdsTests
{
    [Fact]
    public void Ids_are_unique_and_reject_the_empty_guid()
    {
        Assert.NotEqual(PaymentId.New(), PaymentId.New());
        Assert.NotEqual(PaymentAttemptId.New(), PaymentAttemptId.New());
        Assert.NotEqual(RefundId.New(), RefundId.New());
        DomainAssert.Throws(CommonErrors.EmptyId, () => PaymentId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => PaymentAttemptId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => RefundId.From(Guid.Empty));
    }

    [Fact]
    public void Ids_round_trip_their_value()
    {
        var value = Guid.NewGuid();

        Assert.Equal(value, PaymentId.From(value).Value);
        Assert.Equal(value, PaymentAttemptId.From(value).Value);
        Assert.Equal(value, RefundId.From(value).Value);
        Assert.Equal(value.ToString(), RefundId.From(value).ToString());
    }
}

public class OrderReferenceTests
{
    [Fact]
    public void Create_keeps_tenant_order_and_amount_due()
    {
        var tenant = TenantId.New();
        var orderId = OrderId.New();

        var location = LocationId.New();
        var order = OrderReference.Create(tenant, orderId, location, Given.Eur(42.50m));

        Assert.Equal(tenant, order.TenantId);
        Assert.Equal(orderId, order.OrderId);
        Assert.Equal(location, order.LocationId);
        Assert.Equal(Given.Eur(42.50m), order.AmountDue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_rejects_an_order_with_nothing_left_to_pay(int amountDue) =>
        DomainAssert.Throws(
            PaymentErrors.OrderAmountDueNotPositive,
            () => OrderReference.Create(TenantId.New(), OrderId.New(), Given.Location, Given.Eur(amountDue)));

    [Fact]
    public void Create_requires_an_amount_due() =>
        Assert.Throws<ArgumentNullException>(() => OrderReference.Create(TenantId.New(), OrderId.New(), Given.Location, null!));

    [Fact]
    public void References_are_equal_by_value()
    {
        var tenant = TenantId.New();
        var orderId = OrderId.New();

        Assert.Equal(OrderReference.Create(tenant, orderId, Given.Location, Given.Eur(5m)), OrderReference.Create(tenant, orderId, Given.Location, Given.Eur(5m)));
        Assert.NotEqual(OrderReference.Create(tenant, orderId, Given.Location, Given.Eur(5m)), OrderReference.Create(tenant, orderId, Given.Location, Given.Eur(6m)));
        Assert.NotEqual(OrderReference.Create(tenant, orderId, Given.Location, Given.Eur(5m)), OrderReference.Create(tenant, orderId, LocationId.New(), Given.Eur(5m)));
    }
}

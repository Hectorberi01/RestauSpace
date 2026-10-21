namespace Staff.Domain;

public sealed class PermissionCodeTests
{
    [Theory]
    [InlineData("orders.cancel")]
    [InlineData("orders.items.void")]
    [InlineData("kitchen_tickets.bump")]
    [InlineData("reports.export2")]
    public void Create_accepts_lowercase_dotted_segments(string value)
    {
        Assert.Equal(value, PermissionCode.Create(value).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("orders")]
    [InlineData("Orders.cancel")]
    [InlineData("orders.Cancel")]
    [InlineData("orders..cancel")]
    [InlineData(".orders.cancel")]
    [InlineData("orders.cancel.")]
    [InlineData("orders cancel")]
    [InlineData("orders.can cel")]
    [InlineData("orders-cancel")]
    [InlineData("orders.cancel-all")]
    [InlineData("orders:cancel")]
    [InlineData("1orders.cancel")]
    [InlineData("orders.1cancel")]
    [InlineData("_orders.cancel")]
    [InlineData("orders.*")]
    [InlineData("commandes.annulé")]
    public void Create_rejects_an_invalid_format(string? value)
    {
        DomainAssert.Throws(RoleErrors.PermissionCodeInvalid, () => PermissionCode.Create(value));
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        Assert.Equal("orders.cancel", PermissionCode.Create("  orders.cancel ").Value);
    }

    [Fact]
    public void Create_enforces_the_length_limit()
    {
        var atLimit = "a." + new string('b', PermissionCode.MaxLength - 2);

        Assert.Equal(PermissionCode.MaxLength, PermissionCode.Create(atLimit).Value.Length);
        DomainAssert.Throws(RoleErrors.PermissionCodeInvalid, () => PermissionCode.Create(atLimit + "b"));
    }

    [Fact]
    public void Codes_with_the_same_value_are_equal()
    {
        Assert.Equal(PermissionCode.Create("orders.cancel"), PermissionCode.Create("orders.cancel"));
        Assert.NotEqual(PermissionCode.Create("orders.cancel"), PermissionCode.Create("orders.refund"));
        Assert.Equal("orders.cancel", PermissionCode.Create("orders.cancel").ToString());
    }
}

namespace Staff.Domain;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Create_keeps_distinct_codes_and_answers_Contains()
    {
        var catalog = PermissionCatalog.Of("orders.cancel", "orders.refund", "orders.cancel");

        Assert.Equal(2, catalog.Codes.Count);
        Assert.True(catalog.Contains(PermissionCode.Create("orders.cancel")));
        Assert.False(catalog.Contains(PermissionCode.Create("menu.edit")));
    }

    [Fact]
    public void Create_rejects_an_empty_catalog()
    {
        DomainAssert.Throws(RoleErrors.PermissionCatalogEmpty, () => PermissionCatalog.Create([]));
    }

    [Fact]
    public void Create_rejects_a_null_code_as_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => PermissionCatalog.Create([PermissionCode.Create("orders.cancel"), null!]));
    }

    [Fact]
    public void Of_rejects_an_invalid_code()
    {
        DomainAssert.Throws(RoleErrors.PermissionCodeInvalid, () => PermissionCatalog.Of("orders.cancel", "BAD"));
    }

    [Fact]
    public void Catalogs_with_the_same_codes_are_equal_whatever_the_order()
    {
        Assert.Equal(PermissionCatalog.Of("a.b", "c.d"), PermissionCatalog.Of("c.d", "a.b"));
        Assert.NotEqual(PermissionCatalog.Of("a.b"), PermissionCatalog.Of("a.b", "c.d"));
    }
}

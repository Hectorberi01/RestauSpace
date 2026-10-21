namespace Catalog.Domain;

public sealed class ProductLocationTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();
    private readonly LocationId _cotonou = LocationId.New();
    private readonly LocationId _calavi = LocationId.New();

    private LocationReference At(LocationId id, TenantId? tenant = null) => new(tenant ?? Tenant, id, true);

    private Product Active()
    {
        var product = CatalogRefs.Product(Tenant);
        product.Activate(_clock);
        product.ClearDomainEvents();
        return product;
    }

    [Fact]
    public void A_product_without_locations_is_sold_everywhere()
    {
        var product = Active();

        Assert.True(product.IsSoldAt(_cotonou));
        Assert.True(product.IsSellableAt(_calavi));
        Assert.True(product.IsSellableAt(null));
    }

    [Fact]
    public void A_product_sold_in_some_locations_is_refused_elsewhere()
    {
        var product = Active();

        product.SetLocations([At(_cotonou)], _clock);

        Assert.Equal([_cotonou.Value], DomainAssert.Raised<ProductLocationsChanged>(product).LocationIds);
        Assert.True(product.IsSellableAt(_cotonou));
        Assert.False(product.IsSellableAt(_calavi));
        DomainAssert.Throws(ProductErrors.NotSoldHere, () => product.DescribeForSale(null, _calavi));
        Assert.Equal(10m, product.DescribeForSale(null, _cotonou).UnitPrice.Amount);
    }

    [Fact]
    public void Setting_the_same_locations_again_changes_nothing_and_another_restaurant_is_refused()
    {
        var product = Active();
        product.SetLocations([At(_cotonou), At(_calavi)], _clock);
        product.ClearDomainEvents();

        product.SetLocations([At(_calavi), At(_cotonou), At(_calavi)], _clock);

        DomainAssert.NoEvents(product);
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => product.SetLocations([At(LocationId.New(), TenantId.New())], _clock));
    }

    [Fact]
    public void A_shortage_in_one_location_leaves_the_others_selling()
    {
        var product = Active();

        product.SetAvailabilityAt(At(_cotonou), false, _clock);

        Assert.False(DomainAssert.Raised<ProductAvailabilityAtLocationChanged>(product).IsAvailable);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.False(product.IsSellableAt(_cotonou));
        Assert.True(product.IsSellableAt(_calavi));
        DomainAssert.Throws(ProductErrors.UnavailableHere, () => product.DescribeForSale(null, _cotonou));

        product.SetAvailabilityAt(At(_cotonou), true, _clock);
        Assert.True(product.IsSellableAt(_cotonou));
    }

    [Fact]
    public void A_shortage_needs_a_location_where_the_product_is_sold_and_is_forgotten_when_the_location_is_removed()
    {
        var product = Active();
        product.SetLocations([At(_cotonou), At(_calavi)], _clock);
        product.SetAvailabilityAt(At(_calavi), false, _clock);

        product.SetLocations([At(_cotonou)], _clock);

        Assert.Empty(product.UnavailableAt);
        DomainAssert.Throws(ProductErrors.NotSoldHere, () => product.SetAvailabilityAt(At(_calavi), false, _clock));
    }
}

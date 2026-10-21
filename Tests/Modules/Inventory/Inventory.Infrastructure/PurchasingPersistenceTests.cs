using Inventory.Application.Abstractions;
using Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public sealed class PurchasingPersistenceTests(InventoryDatabase database) : IClassFixture<InventoryDatabase>
{
    private FakeClock Clock => database.Clock;

    private static Quantity Kg(decimal amount) => Quantity.Create(amount, UnitOfMeasure.Kilogram);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    private async Task<(InventoryItem Item, LocationReference Place)> GivenItemAsync(TenantId tenantId)
    {
        var place = new LocationReference(tenantId, LocationId.New(), IsOperational: true);
        var ingredient = Ingredient.Create(tenantId, "Farine " + Guid.NewGuid().ToString("N")[..6], UnitOfMeasure.Kilogram, isNameUnique: true);
        var item = InventoryItem.Create(tenantId, place, ingredient.ToReference(), UnitOfMeasure.Kilogram, Kg(1m), isUniqueForLocation: true);
        await SaveAsync(tenantId, services =>
        {
            services.GetRequiredService<IIngredientRepository>().Add(ingredient);
            services.GetRequiredService<IInventoryItemRepository>().Add(item);
        });
        return (item, place);
    }

    [Fact]
    public async Task A_purchase_order_and_its_lines_come_back_whole_after_a_delivery()
    {
        var tenant = TenantId.New();
        var (item, place) = await GivenItemAsync(tenant);
        var supplier = Supplier.Create(tenant, "Grossiste", "+229 01 02 03 04", null, "Livre le mardi", Clock);
        var order = PurchaseOrder.Create(tenant, place, supplier, Clock);
        order.Compose([(item, Kg(10m), 2.5m)], "Urgent");
        order.Send(Clock);
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ISupplierRepository>().Add(supplier);
            services.GetRequiredService<IPurchaseOrderRepository>().Add(order);
        });

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = (await scope.ServiceProvider.GetRequiredService<IPurchaseOrderRepository>().FindAsync(tenant, order.Id))!;
            var stock = (await scope.ServiceProvider.GetRequiredService<IInventoryItemRepository>().FindAsync(tenant, item.Id))!;
            foreach (var receipt in loaded.Receive([(item.Id, 4m, null)], Clock))
            {
                stock.Receive(receipt.Quantity, Clock, loaded.Number, receipt.TotalCost);
            }

            await scope.ServiceProvider.GetRequiredService<IInventoryUnitOfWork>().SaveChangesAsync();
        }

        var back = await InScopeAsync(tenant, services => services.GetRequiredService<IPurchaseOrderRepository>().FindAsync(tenant, order.Id));
        var item2 = await InScopeAsync(tenant, services => services.GetRequiredService<IInventoryItemRepository>().FindAsync(tenant, item.Id));
        var listed = await InScopeAsync(tenant, services =>
            services.GetRequiredService<IPurchaseOrderRepository>().ListAsync(tenant, place.LocationId, PurchaseOrderStatus.PartiallyReceived, 10));
        var suppliers = await InScopeAsync(tenant, services => services.GetRequiredService<ISupplierRepository>().ListAsync(tenant));

        Assert.Equal((PurchaseOrderStatus.PartiallyReceived, "Urgent", order.Number), (back!.Status, back.Notes, back.Number));
        var line = Assert.Single(back.Lines);
        Assert.Equal((10m, 4m, 2.5m), (line.Ordered, line.Received, line.UnitPrice));
        Assert.Equal((4m, 2.5m), (item2!.CurrentQuantity, item2.AverageUnitCost));
        Assert.Equal(order.Id, Assert.Single(listed).Id);
        Assert.Equal("Livre le mardi", Assert.Single(suppliers).Notes);
    }

    [Fact]
    public async Task The_purchase_orders_of_another_tenant_cannot_be_read()
    {
        var tenant = TenantId.New();
        var (item, place) = await GivenItemAsync(tenant);
        var supplier = Supplier.Create(tenant, "Grossiste", null, null, null, Clock);
        var order = PurchaseOrder.Create(tenant, place, supplier, Clock);
        order.Compose([(item, Kg(1m), null)], null);
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ISupplierRepository>().Add(supplier);
            services.GetRequiredService<IPurchaseOrderRepository>().Add(order);
        });
        var other = TenantId.New();

        var found = await InScopeAsync(other, services => services.GetRequiredService<IPurchaseOrderRepository>().FindAsync(tenant, order.Id));
        var supplierFound = await InScopeAsync(other, services => services.GetRequiredService<ISupplierRepository>().FindAsync(other, supplier.Id));

        Assert.Null(found);
        Assert.Null(supplierFound);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Abstractions;
using Orders.Domain;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure;

public sealed class OrdersPersistenceTests(OrdersDatabase database) : IClassFixture<OrdersDatabase>
{
    private static readonly DateOnly Day = new(2026, 1, 15);

    private IClock Clock => database.Clock;

    private static ProductSnapshot Product(TenantId tenantId, string name = "Poulet DG", decimal price = 3500m, params OrderItemModifier[] modifiers) =>
        ProductSnapshot.Create(
            tenantId, ProductId.New(), name, Money.Of(price, Currency.XOF), TaxSnapshot.Create("TVA_18", "TVA 18 %", 0.18m), modifiers);

    private Order NewOrder(TenantId tenantId, LocationId locationId, string number = "20260115-001", TableSessionId? session = null) =>
        Order.Create(
            tenantId,
            new LocationReference(tenantId, locationId, IsOperational: true),
            OrderNumber.Create(number),
            OrderType.DineIn,
            Currency.XOF,
            PriceTaxMode.TaxInclusive,
            Clock,
            tableSession: session is { } id ? new TableSessionReference(tenantId, id, locationId, IsOpen: true) : null);

    private async Task SaveAsync(Order order)
    {
        await using var scope = database.NewScope(order.TenantId);
        scope.ServiceProvider.GetRequiredService<IOrderRepository>().Add(order);
        await scope.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task An_order_comes_back_whole_with_its_lines_in_order_its_modifiers_discounts_and_totals()
    {
        var tenant = TenantId.New();
        var order = NewOrder(tenant, LocationId.New());
        var cheese = OrderItemModifier.Create(ModifierId.New(), "Fromage", Money.Of(500m, Currency.XOF));
        // Plusieurs lignes dans la même milliseconde : seul l'ordre de la liste fait foi, pas celui des ids.
        var names = new[] { "Poulet DG", "Alloco", "Bissap", "Atassi" };
        foreach (var name in names)
        {
            order.AddItem(Product(tenant, name, 1000m, name == "Alloco" ? new[] { cheese } : Array.Empty<OrderItemModifier>()), Quantity.Create(2), name == "Bissap" ? "bien frais" : null);
        }

        order.ApplyDiscount(OrderDiscount.Create(Money.Of(1000m, Currency.XOF), DiscountSource.Manual, "geste-1", "Retard"));
        await SaveAsync(order);

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id));

        Assert.NotNull(loaded);
        Assert.Equal(names, loaded.Items.Select(item => item.ProductName).ToArray());
        Assert.Equal(cheese, Assert.Single(loaded.Items.Single(item => item.ProductName == "Alloco").Modifiers));
        Assert.Equal("bien frais", loaded.Items.Single(item => item.ProductName == "Bissap").Note);
        Assert.Equal(order.Discounts.Single(), Assert.Single(loaded.Discounts));
        Assert.Equal((order.SubTotal, order.DiscountTotal, order.TaxTotal, order.Total), (loaded.SubTotal, loaded.DiscountTotal, loaded.TaxTotal, loaded.Total));
        Assert.Equal(order.Items.Select(item => item.TaxAmount).ToArray(), loaded.Items.Select(item => item.TaxAmount).ToArray());
        Assert.Equal(TaxSnapshot.Create("TVA_18", "TVA 18 %", 0.18m), loaded.Items.First().Tax);
        Assert.Equal("20260115-001", loaded.Number.Value);
        Assert.Equal(PriceTaxMode.TaxInclusive, loaded.PriceTaxMode);
    }

    [Fact]
    public async Task Changes_to_lines_and_a_cancellation_are_saved_with_their_events_in_the_outbox()
    {
        var tenant = TenantId.New();
        var order = NewOrder(tenant, LocationId.New());
        var first = order.AddItem(Product(tenant), Quantity.Create(1));
        order.AddItem(Product(tenant, "Alloco", 500m), Quantity.Create(3));
        await SaveAsync(order);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id);
            loaded!.RemoveItem(first);
            loaded.Place(Clock);
            await scope.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id);
            loaded!.Cancel("Client parti", Clock);
            await scope.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id));
        Assert.Equal("Alloco", Assert.Single(reloaded!.Items).ProductName);
        Assert.Equal(1500m, reloaded.Total.Amount);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<OrdersDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(OrderCreated), types);
        Assert.Contains(nameof(OrderPlaced), types);
        Assert.Contains(nameof(OrderCancelled), types);
    }

    [Fact]
    public async Task An_order_of_another_tenant_cannot_be_loaded_even_with_its_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var order = NewOrder(tenantA, LocationId.New());
        await SaveAsync(order);

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IOrderRepository>().FindAsync(tenantA, order.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IOrderRepository>().FindAsync(tenantB, order.Id)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<OrdersDbContext>().Orders.ToListAsync()));
    }

    [Fact]
    public async Task Numbers_follow_each_other_per_location_and_day_and_are_unique()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();

        var numbers = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var scope = database.NewScope(tenant);
            return (await scope.ServiceProvider.GetRequiredService<IOrderNumberGenerator>().NextAsync(tenant, location, Day)).Value;
        }));
        var otherLocation = await InScopeAsync(
            tenant, services => services.GetRequiredService<IOrderNumberGenerator>().NextAsync(tenant, LocationId.New(), Day));
        var nextDay = await InScopeAsync(
            tenant, services => services.GetRequiredService<IOrderNumberGenerator>().NextAsync(tenant, location, Day.AddDays(1)));

        Assert.Equal(
            new[] { "20260115-001", "20260115-002", "20260115-003", "20260115-004", "20260115-005" },
            numbers.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("20260115-001", otherLocation.Value);
        Assert.Equal("20260116-001", nextDay.Value);

        // Le même numéro deux fois dans l'établissement : l'index unique refuse la seconde commande.
        await SaveAsync(NewOrder(tenant, location, "A-1"));
        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(NewOrder(tenant, location, "A-1")));
        Assert.Equal(Application.OrdersApplicationErrors.NumberAlreadyUsed, refused.Error);
    }

    [Fact]
    public async Task The_history_finds_an_order_by_the_end_of_its_number_and_its_total()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wanted = NewOrder(tenant, location, $"{today:yyyyMMdd}-021");
        await SaveAsync(wanted);
        await SaveAsync(NewOrder(tenant, location, $"{today:yyyyMMdd}-121"));
        await SaveAsync(NewOrder(tenant, location, $"{today.AddDays(-500):yyyyMMdd}-021"));

        var bySuffix = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>()
            .ListAsync(tenant, new OrderSearch(Number: OrderNumberFilter.Parse("21")), 0, 10));
        var byTotal = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>()
            .ListAsync(tenant, new OrderSearch(Number: OrderNumberFilter.Parse("21"), MinTotal: wanted.Total.Amount + 1), 0, 10));

        Assert.Equal(wanted.Id, Assert.Single(bySuffix.Items).Id);
        Assert.Empty(byTotal.Items);
    }

    [Fact]
    public async Task Open_orders_of_a_table_session_are_found_until_they_are_finalized()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var session = TableSessionId.New();
        var order = NewOrder(tenant, location, "T-1", session);
        await SaveAsync(order);
        await SaveAsync(NewOrder(tenant, location, "T-2"));

        Assert.True(await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>().HasOpenOrdersAsync(tenant, session)));
        var page = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>().ListAsync(tenant, new OrderSearch(LocationId: location, TableSessionId: session), 0, 10));
        Assert.Equal(order.Id, Assert.Single(page.Items).Id);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id);
            loaded!.Cancel("Erreur", Clock);
            await scope.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync();
        }

        Assert.False(await InScopeAsync(tenant, services => services.GetRequiredService<IOrderRepository>().HasOpenOrdersAsync(tenant, session)));
    }

    [Fact]
    public async Task Two_requests_changing_the_same_draft_cannot_both_win()
    {
        var tenant = TenantId.New();
        var order = NewOrder(tenant, LocationId.New());
        var item = order.AddItem(Product(tenant), Quantity.Create(1));
        await SaveAsync(order);

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IOrderRepository>().FindAsync(tenant, order.Id);
        mine!.ChangeItemNote(item, "sans piment");
        theirs!.ChangeItemNote(item, "très pimenté");

        await first.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync();
        // Seule la ligne change : c'est la version de la commande, réécrite, qui arrête la seconde.
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IOrdersUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task The_settled_amount_projection_keeps_the_latest_version_of_each_payment_in_its_tenant()
    {
        var tenant = TenantId.New();
        var other = TenantId.New();
        var order = OrderId.New();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        async Task RecordAsync(TenantId owner, Guid payment, decimal amount, long version)
        {
            await using var scope = database.NewScope(owner);
            await scope.ServiceProvider.GetRequiredService<IOrderPaymentSettlements>()
                .RecordAsync(owner, payment, order, Money.Of(amount, Currency.XOF), version);
        }

        await RecordAsync(tenant, first, 3000m, 10);
        await RecordAsync(tenant, first, 1000m, 12);
        await RecordAsync(tenant, first, 3000m, 11);
        await RecordAsync(tenant, second, 500m, 13);
        await RecordAsync(other, Guid.NewGuid(), 9000m, 14);

        var settled = await InScopeAsync(tenant, services => services.GetRequiredService<IOrderPayments>().SettledAmountAsync(tenant, order, Currency.XOF));
        var elsewhere = await InScopeAsync(other, services => services.GetRequiredService<IOrderPayments>().SettledAmountAsync(other, order, Currency.XOF));

        Assert.Equal(Money.Of(1500m, Currency.XOF), settled);
        Assert.Equal(Money.Of(9000m, Currency.XOF), elsewhere);
    }
}

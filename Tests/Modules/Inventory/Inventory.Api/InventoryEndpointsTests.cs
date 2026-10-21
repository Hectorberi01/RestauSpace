using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.Api;

public sealed class InventoryEndpointsTests(InventoryApi api) : IClassFixture<InventoryApi>
{
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await AssertStatusAsync(response, status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Un restaurant, son établissement, l'ingrédient « Farine » (g) et son article en kilogrammes.</summary>
    private async Task<(TenantId Tenant, HttpClient Client, Guid Location, Guid Flour, Guid Item)> GivenStockAsync()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var location = api.NewLocation(tenant);
        var flour = await CreatedIdAsync(await client.PostAsJsonAsync("/inventory/ingredients", new { name = "Farine", baseUnit = "Gram" }));
        var item = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/inventory/items", new { locationId = location, ingredientId = flour, unit = "Kilogram", reorderThreshold = new { amount = 2 } }));
        return (tenant, client, location, flour, item);
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/inventory/items"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/inventory/items"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stock_moves_through_receipts_waste_and_counts_and_keeps_its_ledger()
    {
        var (_, client, location, _, item) = await GivenStockAsync();

        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 10 }, reference = "BL-1" }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/consumptions", new { quantity = new { amount = 500, unit = "Gram" } }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/waste", new { quantity = new { amount = 1 }, reason = "Sac percé" }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/counts", new { countedQuantity = new { amount = 1.5 }, reason = "Inventaire" }),
            HttpStatusCode.NoContent);
        var stock = await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{item}");
        var ledger = (await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{item}/movements")).GetProperty("items");
        var lowStock = await client.GetFromJsonAsync<JsonElement>($"/inventory/items?locationId={location}&lowStockOnly=true");

        Assert.Equal(1.5m, stock.GetProperty("currentQuantity").GetDecimal());
        Assert.Equal("Farine", stock.GetProperty("ingredientName").GetString());
        Assert.True(stock.GetProperty("isLowStock").GetBoolean());
        Assert.Equal(
            new[] { "Adjustment", "Waste", "Consumption", "Receipt" },
            ledger.EnumerateArray().Select(movement => movement.GetProperty("type").GetString()!).ToArray());
        Assert.Equal(item, Assert.Single(lowStock.EnumerateArray().ToList()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Business_rules_answer_with_their_codes()
    {
        var (_, client, location, flour, item) = await GivenStockAsync();

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/consumptions", new { quantity = new { amount = 1 } }),
            HttpStatusCode.UnprocessableEntity,
            "inventory.item.insufficient_stock");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 1, unit = "Liter" } }),
            HttpStatusCode.UnprocessableEntity,
            "inventory.unit.incompatible");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/inventory/items", new { locationId = location, ingredientId = flour }),
            HttpStatusCode.Conflict,
            "inventory.item.not_unique_for_location");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/inventory/ingredients", new { name = "FARINE", baseUnit = "Gram" }),
            HttpStatusCode.Conflict,
            "inventory.ingredient.name_not_unique");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/waste", new { quantity = new { amount = 1 } }),
            HttpStatusCode.UnprocessableEntity,
            "inventory.item.movement_reason_required");
        await AssertProblemAsync(
            await client.GetAsync($"/inventory/items/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "inventory.item.not_found");
    }

    [Fact]
    public async Task Stock_is_transferred_between_two_locations_of_the_restaurant()
    {
        var (tenant, client, _, flour, item) = await GivenStockAsync();
        var annex = api.NewLocation(tenant);
        var annexItem = await CreatedIdAsync(await client.PostAsJsonAsync("/inventory/items", new { locationId = annex, ingredientId = flour }));
        await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 5 } });

        await AssertStatusAsync(
            await client.PostAsJsonAsync(
                "/inventory/transfers",
                new { sourceItemId = item, destinationItemId = annexItem, quantity = new { amount = 2 }, reference = "TR-1" }),
            HttpStatusCode.NoContent);
        var source = await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{item}");
        var destination = await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{annexItem}");

        Assert.Equal(3m, source.GetProperty("currentQuantity").GetDecimal());
        // L'annexe compte en grammes (unité de l'ingrédient).
        Assert.Equal(2000m, destination.GetProperty("currentQuantity").GetDecimal());
    }

    [Fact]
    public async Task An_order_in_preparation_consumes_its_recipe_once()
    {
        var (tenant, client, location, flour, item) = await GivenStockAsync();
        var (bread, _) = api.NewProduct(tenant);
        await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 5 } });
        var recipe = await CreatedIdAsync(await client.PostAsJsonAsync("/inventory/recipes", new { productId = bread }));
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/recipes/{recipe}/ingredients", new { ingredientId = flour, quantity = new { amount = 300 } }),
            HttpStatusCode.NoContent);
        var order = api.NewOrder(tenant, location, [(bread, 2)]);

        var first = await client.PostAsync($"/inventory/orders/{order}/consumption", null);
        var second = await client.PostAsync($"/inventory/orders/{order}/consumption", null);
        var stock = await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{item}");
        var recipes = await client.GetFromJsonAsync<JsonElement>($"/inventory/recipes?productId={bread}");

        await AssertStatusAsync(first, HttpStatusCode.OK);
        Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyConsumed").GetBoolean());
        Assert.True((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyConsumed").GetBoolean());
        // 2 × 300 g = 0,6 kg retirés des 5 kg.
        Assert.Equal(4.4m, stock.GetProperty("currentQuantity").GetDecimal());
        var line = Assert.Single(Assert.Single(recipes.EnumerateArray().ToList()).GetProperty("ingredients").EnumerateArray().ToList());
        Assert.Equal(("Farine", 300m), (line.GetProperty("ingredientName").GetString(), line.GetProperty("quantity").GetDecimal()));
    }

    [Fact]
    public async Task Another_restaurant_sees_nothing_and_a_member_without_permission_only_reads()
    {
        var (tenant, client, _, _, item) = await GivenStockAsync();
        var other = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await other.GetAsync($"/inventory/items/{item}"), HttpStatusCode.NotFound);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/inventory/ingredients")).EnumerateArray());
        api.WithoutPermissions[tenant] = true;
        await AssertStatusAsync(await client.GetAsync($"/inventory/items/{item}"), HttpStatusCode.OK);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 1 } }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/inventory/ingredients", new { name = "Sel", baseUnit = "Gram" }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_stock_but_moves_nothing()
    {
        var (tenant, client, _, _, item) = await GivenStockAsync();
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/inventory/items/{item}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/inventory/items/{item}/receipts", new { quantity = new { amount = 1 } }),
            HttpStatusCode.UnprocessableEntity,
            "inventory.tenant.not_writable");
    }
}

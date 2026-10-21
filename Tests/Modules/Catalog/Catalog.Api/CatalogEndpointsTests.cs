using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Catalog.Api;

public sealed class CatalogEndpointsTests(CatalogApi api) : IClassFixture<CatalogApi>
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

    private static async Task<Guid> CreateProductAsync(HttpClient client, string name = "Poulet DG", decimal basePrice = 3500m)
    {
        var taxCategory = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/catalog/tax-categories", new { code = $"T{Guid.NewGuid():N}"[..10], name = "TVA 18 %", rate = 0.18m }));
        return await CreatedIdAsync(await client.PostAsJsonAsync(
            "/catalog/products", new { name, basePrice, taxCategoryId = taxCategory }));
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/catalog/products"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/catalog/products"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await api.ActingFor(null).PostAsJsonAsync("/catalog/modifier-groups", new { name = "Sauces", maxSelections = 1 }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_restaurant_builds_a_product_with_a_variant_and_options_and_reads_it_back()
    {
        var client = api.ActingFor(api.NewTenant());
        var product = await CreateProductAsync(client);
        var group = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/catalog/modifier-groups", new { name = "Cuisson", minSelections = 1, maxSelections = 1 }));
        await CreatedIdAsync(await client.PostAsJsonAsync($"/catalog/modifier-groups/{group}/modifiers", new { name = "À point", priceDelta = 0 }));

        var variant = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/catalog/products/{product}/variants", new { name = "Grande", priceDelta = 1500, sku = "dg-l" }));
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/catalog/products/{product}/modifier-groups", new { modifierGroupId = group }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/catalog/products/{product}/activate", null), HttpStatusCode.NoContent);

        var read = await client.GetFromJsonAsync<JsonElement>($"/catalog/products/{product}");
        var list = await client.GetFromJsonAsync<JsonElement>("/catalog/products");

        Assert.Equal("Active", read.GetProperty("status").GetString());
        Assert.Equal("XOF", read.GetProperty("currencyCode").GetString());
        Assert.Equal(3500m, read.GetProperty("basePrice").GetDecimal());
        var readVariant = Assert.Single(read.GetProperty("variants").EnumerateArray().ToList());
        Assert.Equal(variant, readVariant.GetProperty("id").GetGuid());
        Assert.Equal(5000m, readVariant.GetProperty("price").GetDecimal());
        Assert.Equal("DG-L", readVariant.GetProperty("sku").GetString());
        Assert.Equal(group, Assert.Single(read.GetProperty("modifierGroupIds").EnumerateArray().ToList()).GetGuid());
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var client = api.ActingFor(api.NewTenant());
        var product = await CreateProductAsync(client);

        // Pas de décimales en franc CFA ; catégorie fiscale inconnue ; produit inconnu.
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/catalog/products/{product}/base-price", new { basePrice = 3500.5m }),
            HttpStatusCode.UnprocessableEntity,
            "common.money.invalid_precision");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/catalog/products", new { name = "Alloco", basePrice = 500, taxCategoryId = Guid.NewGuid() }),
            HttpStatusCode.NotFound,
            "catalog.tax_category.not_found");
        await AssertProblemAsync(
            await client.GetAsync($"/catalog/products/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "catalog.product.not_found");
        await AssertProblemAsync(
            await client.GetAsync("/catalog/products?pageSize=500"), HttpStatusCode.UnprocessableEntity, "catalog.paging.invalid");
    }

    [Fact]
    public async Task A_menu_is_composed_published_and_then_read_only()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var product = await CreateProductAsync(client);
        await AssertStatusAsync(await client.PostAsync($"/catalog/products/{product}/activate", null), HttpStatusCode.NoContent);
        var location = api.NewLocation(tenant);

        var menu = await CreatedIdAsync(await client.PostAsJsonAsync("/catalog/menus", new { locationId = location, name = "Carte du soir" }));
        var section = await CreatedIdAsync(await client.PostAsJsonAsync($"/catalog/menus/{menu}/sections", new { name = "Plats" }));
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/catalog/menus/{menu}/sections/{section}/products", new { productId = product }),
            HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/catalog/menus/{menu}/publish", null), HttpStatusCode.NoContent);

        var read = await client.GetFromJsonAsync<JsonElement>($"/catalog/menus/{menu}");
        var atLocation = await client.GetFromJsonAsync<JsonElement>($"/catalog/menus?locationId={location}");

        Assert.Equal("Published", read.GetProperty("status").GetString());
        Assert.Equal(location, read.GetProperty("locationId").GetGuid());
        var readSection = Assert.Single(read.GetProperty("sections").EnumerateArray().ToList());
        Assert.Equal(product, Assert.Single(readSection.GetProperty("productIds").EnumerateArray().ToList()).GetGuid());
        Assert.Equal(menu, Assert.Single(atLocation.EnumerateArray().ToList()).GetProperty("id").GetGuid());
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/catalog/menus/{menu}/name", new { name = "Autre" }),
            HttpStatusCode.UnprocessableEntity,
            "catalog.menu.published_is_read_only");
    }

    [Fact]
    public async Task A_location_of_another_restaurant_cannot_receive_a_menu()
    {
        var client = api.ActingFor(api.NewTenant());
        var foreign = api.NewLocation(api.NewTenant());

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/catalog/menus", new { locationId = foreign, name = "Carte" }),
            HttpStatusCode.NotFound,
            "catalog.location.not_found");
    }

    [Fact]
    public async Task The_catalog_of_another_restaurant_is_invisible()
    {
        var owner = api.ActingFor(api.NewTenant());
        var product = await CreateProductAsync(owner);
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/catalog/products/{product}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/catalog/products/{product}/archive", null), HttpStatusCode.NotFound);
        var list = await intruder.GetFromJsonAsync<JsonElement>("/catalog/products");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal("Draft", (await owner.GetFromJsonAsync<JsonElement>($"/catalog/products/{product}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Writes_need_the_catalog_permission_but_reads_stay_open_to_members()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var product = await CreateProductAsync(client);
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.GetAsync($"/catalog/products/{product}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.GetAsync("/catalog/tax-categories"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.PostAsync($"/catalog/products/{product}/activate", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/catalog/modifier-groups", new { name = "Sauces", maxSelections = 1 }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_catalog_but_cannot_change_it()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var product = await CreateProductAsync(client);
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/catalog/products/{product}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsync($"/catalog/products/{product}/activate", null),
            HttpStatusCode.UnprocessableEntity,
            "catalog.tenant.not_writable");
    }
}

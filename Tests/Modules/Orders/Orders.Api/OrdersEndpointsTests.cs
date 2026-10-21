using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orders.Api;

public sealed class OrdersEndpointsTests(OrdersApi api) : IClassFixture<OrdersApi>
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

    /// <summary>Un restaurant, son établissement, un produit, et un brouillon à emporter d'une ligne de 2 x 3 500 XOF.</summary>
    private async Task<(TenantId Tenant, HttpClient Client, Guid Order, Guid Item)> GivenDraftAsync()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var order = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/orders", new { locationId = api.NewLocation(tenant), type = "Takeaway" }));
        var item = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/orders/{order}/items", new { productId = api.NewProduct(tenant), quantity = 2, note = "sans piment" }));
        return (tenant, client, order, item);
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/orders"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/orders"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_order_is_taken_followed_and_closed_once_paid()
    {
        var (_, client, order, item) = await GivenDraftAsync();

        await AssertStatusAsync(await client.PutAsJsonAsync($"/orders/{order}/items/{item}/quantity", new { quantity = 3 }), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/orders/{order}/place", null), HttpStatusCode.NoContent);
        foreach (var step in new[] { "accept", "start-preparation", "ready" })
        {
            await AssertStatusAsync(await client.PostAsync($"/orders/{order}/{step}", null), HttpStatusCode.NoContent);
        }

        await AssertProblemAsync(await client.PostAsync($"/orders/{order}/complete", null), HttpStatusCode.UnprocessableEntity, "orders.order.not_settled");
        api.Paid[OrderId.From(order)] = 10_500m;
        await AssertStatusAsync(await client.PostAsync($"/orders/{order}/complete", null), HttpStatusCode.NoContent);

        var read = await client.GetFromJsonAsync<JsonElement>($"/orders/{order}");
        Assert.Equal("Completed", read.GetProperty("status").GetString());
        Assert.Equal("TaxInclusive", read.GetProperty("priceTaxMode").GetString());
        Assert.Equal(10_500m, read.GetProperty("total").GetDecimal());
        Assert.Equal(1602m, read.GetProperty("taxTotal").GetDecimal());
        var line = Assert.Single(read.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("sans piment", line.GetProperty("note").GetString());
        Assert.EndsWith("-001", read.GetProperty("number").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_manual_discount_is_applied_and_removed_by_its_reference()
    {
        var (_, client, order, _) = await GivenDraftAsync();

        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/orders/{order}/discounts", new { amount = 1000, reference = "geste-1", label = "Retard" }),
            HttpStatusCode.NoContent);
        var discounted = await client.GetFromJsonAsync<JsonElement>($"/orders/{order}");
        await AssertStatusAsync(await client.DeleteAsync($"/orders/{order}/discounts/Manual/geste-1"), HttpStatusCode.NoContent);
        var restored = await client.GetFromJsonAsync<JsonElement>($"/orders/{order}");

        Assert.Equal(6000m, discounted.GetProperty("total").GetDecimal());
        Assert.Equal("Manual", Assert.Single(discounted.GetProperty("discounts").EnumerateArray().ToList()).GetProperty("source").GetString());
        Assert.Equal(7000m, restored.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var (tenant, client, order, _) = await GivenDraftAsync();
        await AssertStatusAsync(await client.PostAsync($"/orders/{order}/place", null), HttpStatusCode.NoContent);

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/orders/{order}/items", new { productId = api.NewProduct(tenant), quantity = 1 }),
            HttpStatusCode.UnprocessableEntity,
            "orders.order.not_editable");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/orders/{order}/cancel", new { reason = "" }),
            HttpStatusCode.UnprocessableEntity,
            "orders.order.cancellation_reason_invalid");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/orders", new { locationId = Guid.NewGuid(), type = "Takeaway" }),
            HttpStatusCode.NotFound,
            "orders.location.not_found");
        await AssertProblemAsync(
            await client.GetAsync("/orders?pageSize=500"), HttpStatusCode.UnprocessableEntity, "orders.paging.invalid");
    }

    [Fact]
    public async Task The_orders_of_another_restaurant_are_invisible()
    {
        var (_, owner, order, _) = await GivenDraftAsync();
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/orders/{order}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/orders/{order}/place", null), HttpStatusCode.NotFound);
        Assert.Equal(0, (await intruder.GetFromJsonAsync<JsonElement>("/orders")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/orders")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Writes_need_their_permission_but_reads_stay_open_to_members()
    {
        var (tenant, client, order, _) = await GivenDraftAsync();
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.GetAsync($"/orders/{order}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.PostAsync($"/orders/{order}/place", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/orders/{order}/discounts", new { amount = 100, reference = "x" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsJsonAsync($"/orders/{order}/cancel", new { reason = "test" }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_orders_but_cannot_take_new_ones()
    {
        var (tenant, client, order, _) = await GivenDraftAsync();
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/orders/{order}"), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PostAsync($"/orders/{order}/place", null), HttpStatusCode.UnprocessableEntity, "orders.tenant.not_writable");
    }
}

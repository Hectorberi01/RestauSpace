using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kitchen.Api;

public sealed class KitchenEndpointsTests(KitchenApi api) : IClassFixture<KitchenApi>
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

    /// <summary>Un restaurant, son établissement, un poste « Grill » par défaut et un poste « Bar ».</summary>
    private async Task<(TenantId Tenant, HttpClient Client, Guid Location, Guid Grill, Guid Bar)> GivenKitchenAsync()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var location = api.NewLocation(tenant);
        var grill = await CreatedIdAsync(await client.PostAsJsonAsync("/kitchen/stations", new { locationId = location, name = "Grill" }));
        var bar = await CreatedIdAsync(await client.PostAsJsonAsync("/kitchen/stations", new { locationId = location, name = "Bar" }));
        await CreatedIdAsync(await client.PostAsJsonAsync($"/kitchen/routings/{location}", new { defaultStationId = grill }));
        return (tenant, client, location, grill, bar);
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/kitchen/tickets"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/kitchen/tickets"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_kitchen_is_configured_with_stations_and_routes()
    {
        var (tenant, client, location, grill, bar) = await GivenKitchenAsync();
        var juice = api.NewProduct(tenant);

        await AssertStatusAsync(await client.PutAsJsonAsync($"/kitchen/routings/{location}/routes/{juice}", new { stationId = bar }), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PutAsJsonAsync($"/kitchen/stations/{grill}/name", new { name = "Plancha" }), HttpStatusCode.NoContent);
        var routing = await client.GetFromJsonAsync<JsonElement>($"/kitchen/routings/{location}");
        var stations = await client.GetFromJsonAsync<JsonElement>($"/kitchen/stations?locationId={location}");

        Assert.Equal(grill, routing.GetProperty("defaultStationId").GetGuid());
        var route = Assert.Single(routing.GetProperty("routes").EnumerateArray().ToList());
        Assert.Equal(juice, route.GetProperty("productId").GetGuid());
        Assert.Equal(bar, route.GetProperty("stationId").GetGuid());
        Assert.Equal(
            new[] { "Bar", "Plancha" },
            stations.EnumerateArray().Select(station => station.GetProperty("name").GetString()!).ToArray());

        await AssertProblemAsync(
            await client.PostAsync($"/kitchen/stations/{bar}/deactivate", null), HttpStatusCode.UnprocessableEntity, "kitchen.station.in_use");
        await AssertStatusAsync(await client.DeleteAsync($"/kitchen/routings/{location}/routes/{juice}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/kitchen/stations/{bar}/deactivate", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/kitchen/stations/{bar}/activate", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PutAsJsonAsync($"/kitchen/routings/{location}/default-station", new { stationId = bar }), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_ticket_is_prepared_bumped_and_handed_over()
    {
        var (tenant, client, location, grill, _) = await GivenKitchenAsync();
        var order = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) });

        var ticket = await CreatedIdAsync(await client.PostAsJsonAsync("/kitchen/tickets", new { orderId = order }));
        var again = await CreatedIdAsync(await client.PostAsJsonAsync("/kitchen/tickets", new { orderId = order }));
        var queued = await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets/{ticket}");
        var item = queued.GetProperty("items")[0].GetProperty("id").GetGuid();
        await AssertStatusAsync(await client.PutAsJsonAsync($"/kitchen/tickets/{ticket}/priority", new { priority = "Rush" }), HttpStatusCode.NoContent);
        foreach (var step in new[] { $"items/{item}/start", $"items/{item}/ready", "ready", "complete" })
        {
            await AssertStatusAsync(await client.PostAsync($"/kitchen/tickets/{ticket}/{step}", null), HttpStatusCode.NoContent);
        }

        var done = await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets/{ticket}");
        Assert.Equal(ticket, again);
        Assert.Equal("Queued", queued.GetProperty("status").GetString());
        Assert.Equal(grill, queued.GetProperty("items")[0].GetProperty("stationId").GetGuid());
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal("Rush", done.GetProperty("priority").GetString());
        Assert.Equal("Ready", done.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets?locationId={location}")).EnumerateArray());
    }

    [Fact]
    public async Task The_kitchen_screen_lists_open_tickets_of_a_station()
    {
        var (tenant, client, location, grill, bar) = await GivenKitchenAsync();
        var first = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }) }));
        var cancelled = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }) }));
        await AssertStatusAsync(await client.PostAsJsonAsync($"/kitchen/tickets/{cancelled}/cancel", new { reason = "Rupture" }), HttpStatusCode.NoContent);
        await AssertProblemAsync(
            await client.PostAsync($"/kitchen/tickets/{cancelled}/cancel", null), HttpStatusCode.UnprocessableEntity, "kitchen.ticket.closed");

        var atGrill = await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets?locationId={location}&stationId={grill}");
        var atBar = await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets?locationId={location}&stationId={bar}");

        Assert.Equal(first, Assert.Single(atGrill.EnumerateArray().ToList()).GetProperty("id").GetGuid());
        Assert.Empty(atBar.EnumerateArray());
        var read = await client.GetFromJsonAsync<JsonElement>($"/kitchen/tickets/{cancelled}");
        Assert.Equal("Rupture", read.GetProperty("cancellationReason").GetString());
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var (tenant, client, location, _, _) = await GivenKitchenAsync();

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/kitchen/stations", new { locationId = location, name = "grill" }),
            HttpStatusCode.Conflict,
            "kitchen.station.name_not_unique");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }, isAccepted: false) }),
            HttpStatusCode.UnprocessableEntity,
            "kitchen.order.not_accepted");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/kitchen/tickets", new { orderId = Guid.NewGuid() }), HttpStatusCode.NotFound, "kitchen.order.not_found");
        await AssertProblemAsync(
            await client.GetAsync($"/kitchen/routings/{api.NewLocation(tenant)}"), HttpStatusCode.NotFound, "kitchen.station_routing.not_found");
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/kitchen/routings/{location}/routes/{Guid.NewGuid()}", new { stationId = Guid.NewGuid() }),
            HttpStatusCode.NotFound,
            "kitchen.station.not_found");
    }

    [Fact]
    public async Task The_kitchen_of_another_restaurant_is_invisible()
    {
        var (tenant, owner, location, grill, _) = await GivenKitchenAsync();
        var ticket = await CreatedIdAsync(await owner.PostAsJsonAsync(
            "/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }) }));
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/kitchen/tickets/{ticket}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/kitchen/tickets/{ticket}/start", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/kitchen/stations/{grill}/deactivate", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.GetAsync($"/kitchen/routings/{location}"), HttpStatusCode.NotFound);
        Assert.Empty((await intruder.GetFromJsonAsync<JsonElement>("/kitchen/tickets")).EnumerateArray());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>("/kitchen/tickets")).EnumerateArray());
    }

    [Fact]
    public async Task Writes_need_their_permission_but_reads_stay_open_to_members()
    {
        var (tenant, client, location, grill, _) = await GivenKitchenAsync();
        var ticket = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }) }));
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.GetAsync($"/kitchen/tickets/{ticket}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.GetAsync($"/kitchen/routings/{location}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.PostAsync($"/kitchen/tickets/{ticket}/start", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/kitchen/stations", new { locationId = location, name = "Bar 2" }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsync($"/kitchen/stations/{grill}/deactivate", null), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_kitchen_but_cannot_change_it()
    {
        var (tenant, client, location, _, _) = await GivenKitchenAsync();
        var ticket = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/kitchen/tickets", new { orderId = api.NewOrder(tenant, location, new[] { api.NewProduct(tenant) }) }));
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/kitchen/tickets/{ticket}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsync($"/kitchen/tickets/{ticket}/start", null), HttpStatusCode.UnprocessableEntity, "kitchen.tenant.not_writable");
    }
}

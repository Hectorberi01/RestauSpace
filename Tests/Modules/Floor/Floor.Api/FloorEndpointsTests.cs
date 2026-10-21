using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Floor.Api;

public sealed class FloorEndpointsTests(FloorApi api) : IClassFixture<FloorApi>
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

    /// <summary>Un restaurant, un établissement, une zone « Terrasse » et une table « T1 » de 4 places.</summary>
    private async Task<(TenantId Tenant, HttpClient Client, Guid Location, Guid Area, Guid Table)> GivenTableAsync()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var location = api.NewLocation(tenant);
        var area = await CreatedIdAsync(await client.PostAsJsonAsync("/floor/dining-areas", new { locationId = location, name = "Terrasse" }));
        var table = await CreatedIdAsync(await client.PostAsJsonAsync($"/floor/dining-areas/{area}/tables", new { label = "T1", capacity = 4 }));
        return (tenant, client, location, area, table);
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/floor/dining-areas"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/floor/dining-areas"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await api.ActingFor(null).PostAsJsonAsync("/floor/table-sessions", new { tableId = Guid.NewGuid(), guestCount = 2 }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_restaurant_lays_out_its_floor_seats_guests_and_frees_the_table()
    {
        var (_, client, location, area, table) = await GivenTableAsync();

        var session = await CreatedIdAsync(await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 3 }));
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/floor/table-sessions/{session}/guest-count", new { guestCount = 5 }), HttpStatusCode.NoContent);

        var plan = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/floor/dining-areas?locationId={location}")).EnumerateArray().ToList());
        var readTable = Assert.Single(plan.GetProperty("tables").EnumerateArray().ToList());
        Assert.Equal(area, plan.GetProperty("id").GetGuid());
        Assert.Equal("T1", readTable.GetProperty("label").GetString());
        Assert.Equal("Available", readTable.GetProperty("status").GetString());
        Assert.Equal(session, readTable.GetProperty("openSessionId").GetGuid());
        var open = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/floor/table-sessions")).EnumerateArray().ToList());
        Assert.Equal(5, open.GetProperty("guestCount").GetInt32());
        Assert.True(open.GetProperty("reservationId").ValueKind == JsonValueKind.Null);

        await AssertStatusAsync(await client.PostAsync($"/floor/table-sessions/{session}/close", null), HttpStatusCode.NoContent);
        var closed = await client.GetFromJsonAsync<JsonElement>($"/floor/table-sessions/{session}");
        Assert.False(closed.GetProperty("isOpen").GetBoolean());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/floor/table-sessions")).EnumerateArray().ToList());
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var (_, client, _, area, table) = await GivenTableAsync();
        var session = await CreatedIdAsync(await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 2 }));
        api.SessionsWithOpenOrders[TableSessionId.From(session)] = true;

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 2 }),
            HttpStatusCode.Conflict,
            "floor.table_session.table_already_occupied");
        await AssertProblemAsync(
            await client.PostAsync($"/floor/table-sessions/{session}/close", null),
            HttpStatusCode.UnprocessableEntity,
            "floor.table_session.has_open_orders");
        await AssertProblemAsync(
            await client.DeleteAsync($"/floor/dining-areas/{area}/tables/{table}"),
            HttpStatusCode.UnprocessableEntity,
            "floor.dining_area.table_has_open_session");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/floor/dining-areas/{area}/tables", new { label = " t1 ", capacity = 2 }),
            HttpStatusCode.Conflict,
            "floor.dining_area.table_label_already_used");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = Guid.NewGuid(), guestCount = 2 }),
            HttpStatusCode.NotFound,
            "floor.table.not_found");
    }

    [Fact]
    public async Task A_location_of_another_restaurant_cannot_receive_a_dining_area()
    {
        var client = api.ActingFor(api.NewTenant());
        var foreign = api.NewLocation(api.NewTenant());

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/floor/dining-areas", new { locationId = foreign, name = "Terrasse" }),
            HttpStatusCode.NotFound,
            "floor.location.not_found");
    }

    [Fact]
    public async Task The_floor_of_another_restaurant_is_invisible()
    {
        var (_, owner, _, area, table) = await GivenTableAsync();
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/floor/dining-areas/{area}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(
            await intruder.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 2 }), HttpStatusCode.NotFound);
        Assert.Empty((await intruder.GetFromJsonAsync<JsonElement>("/floor/dining-areas")).EnumerateArray().ToList());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>("/floor/dining-areas")).EnumerateArray().ToList());
    }

    [Fact]
    public async Task Writes_need_their_floor_permission_but_reads_stay_open_to_members()
    {
        var (tenant, client, _, area, table) = await GivenTableAsync();
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.GetAsync($"/floor/dining-areas/{area}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.GetAsync("/floor/table-sessions"), HttpStatusCode.OK);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/floor/dining-areas/{area}/tables", new { label = "T2", capacity = 2 }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 2 }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_floor_but_cannot_seat_anyone()
    {
        var (tenant, client, _, area, table) = await GivenTableAsync();
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/floor/dining-areas/{area}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/floor/table-sessions", new { tableId = table, guestCount = 2 }),
            HttpStatusCode.UnprocessableEntity,
            "floor.tenant.not_writable");
    }
}

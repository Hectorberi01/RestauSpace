using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Locations.Api;

public sealed class LocationsEndpointsTests(LocationsApi api) : IClassFixture<LocationsApi>
{
    private static object NewLocation(string name = "Cotonou Centre") => new
    {
        name,
        address = new { street = "Rue 12.345", city = "Cotonou", countryCode = "BJ" },
        seats = 40,
        weeklyHours = new[] { new { day = "Friday", start = "18:00", end = "02:00", endsNextDay = true } },
    };

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

    private static async Task<Guid> CreateAsync(HttpClient client, string name = "Cotonou Centre")
    {
        var response = await client.PostAsJsonAsync("/locations", NewLocation(name));
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/locations"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/locations"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await api.ActingFor(null).PostAsJsonAsync("/locations", NewLocation()), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_restaurant_creates_reads_and_lists_its_locations()
    {
        var client = api.ActingFor(api.NewTenant());

        var id = await CreateAsync(client);
        var location = await client.GetFromJsonAsync<JsonElement>($"/locations/{id}");
        var list = await client.GetFromJsonAsync<JsonElement>("/locations");

        Assert.Equal("Cotonou Centre", location.GetProperty("name").GetString());
        Assert.Equal("Draft", location.GetProperty("status").GetString());
        Assert.Equal("Africa/Porto-Novo", location.GetProperty("timeZoneId").GetString());
        var slot = Assert.Single(location.GetProperty("weeklyHours").EnumerateArray().ToList());
        Assert.Equal("Friday", slot.GetProperty("day").GetString());
        Assert.Equal("02:00", slot.GetProperty("end").GetString());
        Assert.True(slot.GetProperty("endsNextDay").GetBoolean());
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_restaurant_never_sees_nor_changes_the_locations_of_another_one()
    {
        var ofA = await CreateAsync(api.ActingFor(api.NewTenant()), "Chez A");
        var asB = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(await asB.GetAsync($"/locations/{ofA}"), HttpStatusCode.NotFound, "locations.location.not_found");
        await AssertProblemAsync(
            await asB.PutAsJsonAsync($"/locations/{ofA}/name", new { name = "Volée" }),
            HttpStatusCode.NotFound,
            "locations.location.not_found");
        await AssertProblemAsync(
            await asB.PostAsync($"/locations/{ofA}/archive", null), HttpStatusCode.NotFound, "locations.location.not_found");
        Assert.Equal(0, (await asB.GetFromJsonAsync<JsonElement>("/locations")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Lifecycle_and_exceptions_go_through_the_domain_rules()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await CreateAsync(client);

        await AssertStatusAsync(await client.PostAsync($"/locations/{id}/activate", null), HttpStatusCode.NoContent);
        await AssertProblemAsync(
            await client.PostAsync($"/locations/{id}/activate", null),
            HttpStatusCode.UnprocessableEntity,
            "locations.location.only_draft_can_be_activated");
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/locations/{id}/exceptional-closures", new { date = "2026-12-25", reason = "Noël" }),
            HttpStatusCode.NoContent);
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/locations/{id}/exceptional-closures", new { date = "2026-12-25" }),
            HttpStatusCode.Conflict,
            "locations.exceptional_schedule.date_already_defined");
        await AssertStatusAsync(await client.DeleteAsync($"/locations/{id}/exceptions/2026-12-25"), HttpStatusCode.NoContent);

        var location = await client.GetFromJsonAsync<JsonElement>($"/locations/{id}");
        Assert.Equal("Active", location.GetProperty("status").GetString());
        Assert.Empty(location.GetProperty("exceptionalSchedules").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_values_are_refused_with_a_stable_code()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await CreateAsync(client);

        await AssertProblemAsync(
            await client.PutAsJsonAsync(
                $"/locations/{id}/business-hours",
                new { weeklyHours = new[] { new { day = "Monday", start = "18h", end = "22:00" } } }),
            HttpStatusCode.UnprocessableEntity,
            "locations.time.invalid");
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/locations/{id}/capacity", new { seats = -1 }),
            HttpStatusCode.UnprocessableEntity,
            "locations.capacity.negative");
        await AssertProblemAsync(
            await client.GetAsync("/locations?pageSize=500"), HttpStatusCode.UnprocessableEntity, "locations.paging.invalid");
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_locations_but_cannot_change_them()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var id = await CreateAsync(client);
        api.Tenants[tenant] = false;

        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/locations/{id}/name", new { name = "Nouveau" }),
            HttpStatusCode.UnprocessableEntity,
            "locations.tenant.not_writable");
        await AssertStatusAsync(await client.GetAsync($"/locations/{id}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_the_locations_manage_permission_a_member_reads_but_cannot_write()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var id = await CreateAsync(client);
        api.WithoutPermissions[tenant] = true;

        await AssertStatusAsync(await client.PostAsJsonAsync("/locations", NewLocation()), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsync($"/locations/{id}/activate", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync($"/locations/{id}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.GetAsync("/locations"), HttpStatusCode.OK);
    }
}

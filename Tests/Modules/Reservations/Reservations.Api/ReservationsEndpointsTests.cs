using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Reservations.Api;

public sealed class ReservationsEndpointsTests(ReservationsApi api) : IClassFixture<ReservationsApi>
{
    // Demain à 20:00 UTC : dans la fenêtre de la politique par défaut quelle que soit l'heure du test.
    private static readonly DateTimeOffset Tomorrow8Pm = new(DateTime.UtcNow.Date.AddDays(1).AddHours(20), TimeSpan.Zero);

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

    private static object NewReservation(Guid location, int partySize = 4) => new
    {
        locationId = location,
        startAt = Tomorrow8Pm.ToOffset(TimeSpan.FromHours(1)),
        partySize,
        guestName = "Awa Dossou",
        guestPhone = "+22990000000",
    };

    private (TenantId Tenant, HttpClient Client, Guid Location) GivenRestaurant()
    {
        var tenant = api.NewTenant();
        return (tenant, api.ActingFor(tenant), api.NewLocation(tenant));
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync($"/reservations/{Guid.NewGuid()}"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync($"/reservations/{Guid.NewGuid()}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_reservation_goes_from_booking_to_completion()
    {
        var (_, client, location) = GivenRestaurant();

        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reservations", NewReservation(location)));
        await AssertStatusAsync(await client.PostAsync($"/reservations/{id}/confirm", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PutAsJsonAsync($"/reservations/{id}/party-size", new { partySize = 5 }), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/reservations/{id}/seat", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/reservations/{id}/complete", null), HttpStatusCode.NoContent);

        var read = await client.GetFromJsonAsync<JsonElement>($"/reservations/{id}");
        Assert.Equal("Completed", read.GetProperty("status").GetString());
        Assert.Equal(5, read.GetProperty("partySize").GetInt32());
        Assert.Equal(120, read.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(Tomorrow8Pm, read.GetProperty("startAtUtc").GetDateTimeOffset());

        var from = Uri.EscapeDataString(Tomorrow8Pm.Date.ToString("yyyy-MM-ddT00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(Tomorrow8Pm.Date.AddDays(1).ToString("yyyy-MM-ddT00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var agenda = await client.GetFromJsonAsync<JsonElement>($"/reservations?from={from}&to={to}&locationId={location}");
        Assert.Equal(id, Assert.Single(agenda.EnumerateArray().ToList()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_last_seats_are_refused_and_availability_says_so()
    {
        var (_, client, location) = GivenRestaurant();
        api.Seats[LocationId.From(location)] = 6;
        await CreatedIdAsync(await client.PostAsJsonAsync("/reservations", NewReservation(location, partySize: 4)));

        var startAt = Uri.EscapeDataString(Tomorrow8Pm.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var availability = await client.GetFromJsonAsync<JsonElement>(
            $"/reservations/availability?locationId={location}&startAt={startAt}&partySize=3");

        Assert.False(availability.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(2, availability.GetProperty("remainingSeats").GetInt32());
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reservations", NewReservation(location, partySize: 3)),
            HttpStatusCode.UnprocessableEntity,
            "reservations.reservation.capacity_insufficient");
    }

    [Fact]
    public async Task Business_errors_come_back_as_problem_details()
    {
        var (_, client, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reservations", NewReservation(location)));

        await AssertProblemAsync(
            await client.PostAsync($"/reservations/{id}/seat", null), HttpStatusCode.UnprocessableEntity, "reservations.reservation.not_confirmed");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reservations", new { locationId = location, startAt = Tomorrow8Pm, partySize = 2, guestName = "Sans contact" }),
            HttpStatusCode.UnprocessableEntity,
            "reservations.guest_contact.channel_required");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reservations", NewReservation(Guid.NewGuid())),
            HttpStatusCode.NotFound,
            "reservations.location.not_found");
        await AssertProblemAsync(
            await client.GetAsync($"/reservations?from=2026-01-01T00:00:00Z&to=2026-03-01T00:00:00Z"),
            HttpStatusCode.UnprocessableEntity,
            "reservations.window.invalid");
    }

    [Fact]
    public async Task The_policy_of_a_location_is_read_and_replaced()
    {
        var (_, client, location) = GivenRestaurant();

        var initial = await client.GetFromJsonAsync<JsonElement>($"/reservations/policies/{location}");
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/reservations/policies/{location}", new
            {
                minLeadTimeMinutes = 0,
                maxAdvanceDays = 30,
                defaultDurationMinutes = 90,
                cancellationCutoffMinutes = 60,
                minDurationMinutes = 30,
                maxDurationMinutes = 180,
                maxPartySize = 2,
            }),
            HttpStatusCode.NoContent);
        var replaced = await client.GetFromJsonAsync<JsonElement>($"/reservations/policies/{location}");

        Assert.Equal(120, initial.GetProperty("defaultDurationMinutes").GetInt32());
        Assert.Equal(90, replaced.GetProperty("defaultDurationMinutes").GetInt32());
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reservations", NewReservation(location, partySize: 3)),
            HttpStatusCode.UnprocessableEntity,
            "reservations.reservation.party_size_too_large");
    }

    [Fact]
    public async Task The_reservations_of_another_restaurant_are_invisible()
    {
        var (_, owner, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await owner.PostAsJsonAsync("/reservations", NewReservation(location)));
        var intruder = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await intruder.GetAsync($"/reservations/{id}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/reservations/{id}/cancel", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsJsonAsync("/reservations", NewReservation(location)), HttpStatusCode.NotFound);
        Assert.Equal("Pending", (await owner.GetFromJsonAsync<JsonElement>($"/reservations/{id}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Writes_and_guest_details_need_their_permission_but_policies_stay_open_to_members()
    {
        var (tenant, client, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reservations", NewReservation(location)));
        api.WithoutPermissions[tenant] = true;
        var from = DateTimeOffset.UtcNow.ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(7).ToString("O");

        await AssertStatusAsync(await client.GetAsync($"/reservations/policies/{location}"), HttpStatusCode.OK);
        await AssertStatusAsync(await client.GetAsync($"/reservations/{id}"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.GetAsync($"/reservations?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.GetAsync($"/reservations/waitlist?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsync($"/reservations/{id}/confirm", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/reservations/policies/{location}", new { maxAdvanceDays = 30 }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_reservations_but_cannot_book()
    {
        var (tenant, client, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reservations", NewReservation(location)));
        api.Tenants[tenant] = false;

        await AssertStatusAsync(await client.GetAsync($"/reservations/{id}"), HttpStatusCode.OK);
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reservations", NewReservation(location)),
            HttpStatusCode.UnprocessableEntity,
            "reservations.tenant.not_writable");
    }
}

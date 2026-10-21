using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tenants.Api;

public sealed class DemoRequestEndpointsTests(TenantsApi api) : IClassFixture<TenantsApi>
{
    private HttpClient Client => api.Client;

    private static object Form(string restaurant, string? email = "awa@example.com", string? phone = null, string? website = null) => new
    {
        contactName = "Awa Dossou",
        restaurantName = restaurant,
        email,
        phone,
        country = "Bénin",
        planInterest = "Pro",
        message = "Deux salles, 60 couverts.",
        website,
    };

    private async Task<JsonElement?> FindAsync(string restaurant)
    {
        var list = await Client.GetFromJsonAsync<JsonElement>("/admin/demo-requests?pageSize=100");
        foreach (var item in list.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("restaurantName").GetString() == restaurant)
            {
                return item;
            }
        }

        return null;
    }

    [Fact]
    public async Task A_submitted_request_is_listed_then_followed_up_and_deleted()
    {
        var restaurant = $"Chez Awa {Guid.NewGuid():N}"[..20];
        var response = await Client.PostAsJsonAsync("/demo-requests", Form(restaurant, phone: "+229 97 00 00 00"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());

        var item = await FindAsync(restaurant);
        Assert.NotNull(item);
        var id = item.Value.GetProperty("id").GetGuid();
        Assert.Equal("New", item.Value.GetProperty("status").GetString());
        Assert.Equal("+22997000000", item.Value.GetProperty("phone").GetString());

        var update = await Client.PutAsJsonAsync($"/admin/demo-requests/{id}", new { status = "Contacted", note = "Rappel jeudi" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var detail = await Client.GetFromJsonAsync<JsonElement>($"/admin/demo-requests/{id}");
        Assert.Equal("Contacted", detail.GetProperty("status").GetString());
        Assert.Equal("Rappel jeudi", detail.GetProperty("note").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/admin/demo-requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/admin/demo-requests/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_robot_filling_the_hidden_field_gets_the_same_answer_but_nothing_is_saved()
    {
        var restaurant = $"Robot {Guid.NewGuid():N}"[..20];
        var response = await Client.PostAsJsonAsync("/demo-requests", Form(restaurant, website: "http://spam.example"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Null(await FindAsync(restaurant));
    }

    [Fact]
    public async Task A_request_without_any_contact_is_a_422()
    {
        var response = await Client.PostAsJsonAsync("/demo-requests", Form("Chez Awa", email: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenants.demo_request.contact_missing", problem.GetProperty("code").GetString());
    }
}

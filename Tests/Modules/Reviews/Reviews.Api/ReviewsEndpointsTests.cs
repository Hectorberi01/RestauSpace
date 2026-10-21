using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Reviews.Application;

namespace Reviews.Api;

public sealed class ReviewsEndpointsTests(ReviewsApi api) : IClassFixture<ReviewsApi>
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

    private (TenantId Tenant, HttpClient Client, Guid Location) GivenRestaurant()
    {
        var tenant = api.NewTenant();
        return (tenant, api.ActingFor(tenant), api.NewLocation(tenant));
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/reviews/feedbacks"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/reviews/feedbacks"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_feedback_is_submitted_read_reviewed_and_counted()
    {
        var (tenant, client, location) = GivenRestaurant();
        var order = api.NewOrder(tenant, location);

        var id = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/reviews/feedbacks", new { locationId = location, rating = 2, comment = "Attente trop longue", orderId = order }));
        await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 5 }));
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 4, orderId = order }),
            HttpStatusCode.Conflict,
            ReviewsApplicationErrors.OrderAlreadyReviewed.Code);
        await AssertStatusAsync(await client.PostAsync($"/reviews/feedbacks/{id}/reviewed", null), HttpStatusCode.NoContent);

        var read = await client.GetFromJsonAsync<JsonElement>($"/reviews/feedbacks/{id}");
        Assert.Equal("Reviewed", read.GetProperty("status").GetString());
        Assert.Equal("Attente trop longue", read.GetProperty("comment").GetString());
        Assert.Equal(order, read.GetProperty("orderId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, read.GetProperty("reviewedBy").ValueKind);

        var low = (await client.GetFromJsonAsync<JsonElement>($"/reviews/feedbacks?locationId={location}&maxRating=2")).GetProperty("items");
        Assert.Equal(id, Assert.Single(low.EnumerateArray().ToList()).GetProperty("id").GetGuid());
        var summary = await client.GetFromJsonAsync<JsonElement>($"/reviews/feedbacks/summary?locationId={location}");
        Assert.Equal(2, summary.GetProperty("count").GetInt32());
        Assert.Equal(3.5m, summary.GetProperty("averageRating").GetDecimal());
    }

    [Fact]
    public async Task Each_route_requires_its_permission()
    {
        var (tenant, client, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 3 }));
        // Une tablette en salle : peut seulement enregistrer des avis.
        api.Granted[tenant] = [ReviewsPermissions.Submit];

        await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 4 }));
        await AssertStatusAsync(await client.GetAsync("/reviews/feedbacks"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync($"/reviews/feedbacks/{id}"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsync($"/reviews/feedbacks/{id}/archive", null), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsJsonAsync("/reviews/surveys", new { title = "Visite" }), HttpStatusCode.Forbidden);

        // Lecture seule : ni enregistrement ni traitement.
        api.Granted[tenant] = [ReviewsPermissions.View];
        await AssertStatusAsync(await client.GetAsync($"/reviews/feedbacks/{id}"), HttpStatusCode.OK);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 4 }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.PostAsync($"/reviews/feedbacks/{id}/archive", null), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_feedback_or_a_location_of_another_restaurant_is_not_found()
    {
        var (_, client, location) = GivenRestaurant();
        var (_, other, otherLocation) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 3 }));

        await AssertProblemAsync(await other.GetAsync($"/reviews/feedbacks/{id}"), HttpStatusCode.NotFound, ReviewsApplicationErrors.FeedbackNotFound.Code);
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = otherLocation, rating = 3 }),
            HttpStatusCode.NotFound,
            ReviewsApplicationErrors.LocationNotFound.Code);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/reviews/feedbacks")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_feedback_but_cannot_change_it()
    {
        var (tenant, client, location) = GivenRestaurant();
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/feedbacks", new { locationId = location, rating = 3 }));
        api.Tenants[tenant] = false;

        await AssertProblemAsync(
            await client.PostAsync($"/reviews/feedbacks/{id}/archive", null),
            HttpStatusCode.UnprocessableEntity,
            ReviewsApplicationErrors.TenantNotWritable.Code);
        await AssertStatusAsync(await client.GetAsync($"/reviews/feedbacks/{id}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_survey_is_drafted_published_and_frozen()
    {
        var (_, client, _) = GivenRestaurant();

        var survey = await CreatedIdAsync(await client.PostAsJsonAsync("/reviews/surveys", new { title = "Votre visite" }));
        var first = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/reviews/surveys/{survey}/questions", new { text = "Note globale ?", type = "Rating", isRequired = true }));
        var second = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/reviews/surveys/{survey}/questions", new { text = "Plat préféré ?", type = "SingleChoice", choices = new[] { "Poisson", "Poulet" } }));
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/reviews/surveys/{survey}/questions/{second}/position", new { position = 1 }), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/reviews/surveys/{survey}/publish", null), HttpStatusCode.NoContent);
        await AssertProblemAsync(
            await client.DeleteAsync($"/reviews/surveys/{survey}/questions/{first}"),
            HttpStatusCode.UnprocessableEntity,
            "reviews.survey.not_draft");

        var read = await client.GetFromJsonAsync<JsonElement>($"/reviews/surveys/{survey}");
        Assert.Equal("Published", read.GetProperty("status").GetString());
        var questions = read.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(new[] { second, first }, questions.Select(question => question.GetProperty("id").GetGuid()));
        Assert.Equal(2, questions[0].GetProperty("choices").GetArrayLength());
        var published = await client.GetFromJsonAsync<JsonElement>("/reviews/surveys?status=Published");
        Assert.Equal(survey, Assert.Single(published.EnumerateArray().ToList()).GetProperty("id").GetGuid());
    }
}

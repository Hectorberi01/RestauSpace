using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.Api;

internal static class Http
{
    public static Task<JsonElement> ReadAsync(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task AssertStatusAsync(this HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    public static async Task AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.AssertStatusAsync(status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await response.ReadAsync()).GetProperty("code").GetString());
    }

    public static string Text(this JsonElement json, string property) => json.GetProperty(property).GetString()!;
}

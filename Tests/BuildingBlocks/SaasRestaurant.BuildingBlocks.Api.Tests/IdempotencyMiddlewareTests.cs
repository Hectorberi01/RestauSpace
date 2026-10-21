using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

public sealed class IdempotencyMiddlewareTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private int _created;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddSingleton<IClock>(new FakeClock());
        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseIdempotencyKeys();
        _app.MapPost("/payments", (Amount amount) =>
        {
            var number = Interlocked.Increment(ref _created);
            return TypedResults.Created($"/payments/{number}", new { number, amount.Value });
        }).RequireIdempotencyKey();
        _app.MapPost("/notes", (Amount amount) => TypedResults.Ok(new { number = Interlocked.Increment(ref _created) }));
        _app.MapPost("/refused", (Amount amount) =>
        {
            Interlocked.Increment(ref _created);
            throw new DomainException(new DomainError("test.refused", "Refused."));
        });
        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    public sealed record Amount(decimal Value);

    private async Task<HttpResponseMessage> PostAsync(string path, decimal amount, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new Amount(amount)) };
        if (key is not null)
        {
            request.Headers.Add(Idempotency.HeaderName, key);
        }

        return await _app.GetTestClient().SendAsync(request);
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task The_same_key_replays_the_first_response_without_running_the_operation_again()
    {
        var first = await PostAsync("/payments", 10m, "pay-1");
        var second = await PostAsync("/payments", 10m, "pay-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.True(second.Headers.Contains(Idempotency.ReplayedHeaderName));
        Assert.Equal(1, _created);
    }

    [Fact]
    public async Task A_key_reused_for_another_request_is_refused()
    {
        await PostAsync("/payments", 10m, "pay-2");
        var other = await PostAsync("/payments", 99m, "pay-2");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal(Idempotency.KeyReused.Code, await CodeOfAsync(other));
        Assert.Equal(1, _created);
    }

    [Fact]
    public async Task A_required_key_must_be_present_and_valid()
    {
        var missing = await PostAsync("/payments", 10m, null);
        var invalid = await PostAsync("/payments", 10m, new string('k', Idempotency.MaxKeyLength + 1));

        Assert.Equal(Idempotency.KeyRequired.Code, await CodeOfAsync(missing));
        Assert.Equal(Idempotency.KeyInvalid.Code, await CodeOfAsync(invalid));
        Assert.Equal(0, _created);
    }

    [Fact]
    public async Task An_optional_key_is_honoured_when_given_and_ignored_when_absent()
    {
        await PostAsync("/notes", 1m, "note-1");
        await PostAsync("/notes", 1m, "note-1");
        await PostAsync("/notes", 1m, null);
        await PostAsync("/notes", 1m, null);

        Assert.Equal(3, _created);
    }

    [Fact]
    public async Task A_refused_request_frees_its_key_for_a_new_attempt()
    {
        var first = await PostAsync("/refused", 1m, "refused-1");
        var second = await PostAsync("/refused", 1m, "refused-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(2, _created);
    }
}

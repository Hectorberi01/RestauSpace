using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Identity.Api;

/// <summary>
/// Le module tel qu'un hôte le composerait, servi en mémoire sur un vrai PostgreSQL (Docker), avec une vraie validation
/// des tokens. Faux : l'horloge (démarrée au vrai « maintenant », donc les tokens émis sont valides) et la boîte mail.
/// </summary>
public class IdentityApi : IAsyncLifetime, IIdentityNotifier
{
    public const string Password = "correct horse battery";
    public const string AdminEmail = "admin@example.com";

    private static int _sequence;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly List<(string Kind, string Email, string Token)> _sent = [];

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Tous les clients de test ont la même adresse : la limite par adresse est écartée, sauf si une fixture la veut.</summary>
    protected virtual int AuthPermitLimit => 1_000_000;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddIdentityModule(_container.GetConnectionString(), options =>
        {
            options.Issuer = "identity-api-tests";
            options.Audience = "saasrestaurant";
            options.SigningKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray());
        }, limit => limit.PermitLimit = AuthPermitLimit);
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<IIdentityNotifier>(this);
        builder.Services.AddIdentityTenantMemberships<ApiTenantMemberships>();
        builder.Services.AddAuthentication().AddIdentityAccessTokens();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("back-office", policy => policy.RequireClaim(IdentityClaims.Email, AdminEmail));

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapIdentityEndpoints("back-office");

        await _app.Services.MigrateIdentityDatabaseAsync();
        await _app.StartAsync();
    }

    public HttpClient Anonymous() => _app.GetTestClient();

    public HttpClient WithToken(string accessToken)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static string NewEmail() => $"user{Interlocked.Increment(ref _sequence)}@example.com";

    public string LastTokenSentTo(string email)
    {
        lock (_sent)
        {
            return _sent.Last(sent => sent.Email == email).Token;
        }
    }

    public IReadOnlyList<string> KindsSentTo(string email)
    {
        lock (_sent)
        {
            return _sent.Where(sent => sent.Email == email).Select(sent => sent.Kind).ToList();
        }
    }

    /// <summary>Inscrit et vérifie un compte via HTTP.</summary>
    public async Task<Guid> GivenActiveUserAsync(string email, string password = Password)
    {
        var client = Anonymous();
        var registered = await client.PostAsJsonAsync("/auth/register", new { email, password });
        registered.EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/auth/verify-email", new { token = LastTokenSentTo(email), password = password })).EnsureSuccessStatusCode();
        return (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task<JsonElement> LoginAsync(string email, string password = Password)
    {
        var response = await Anonymous().PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Un compte vérifié et connecté : son email, ses tokens et un client authentifié.</summary>
    public async Task<(string Email, JsonElement Tokens, HttpClient Client)> GivenSignedInUserAsync(string? email = null)
    {
        email ??= NewEmail();
        await GivenActiveUserAsync(email);
        var tokens = await LoginAsync(email);
        return (email, tokens, WithToken(tokens.GetProperty("accessToken").GetString()!));
    }

    /// <summary>Session de caisse pour ce compte, comme l'ouvre Staff après un PIN juste (le compte doit être membre).</summary>
    public async Task<OperatorSessionDto> StartTillSessionAsync(Guid userId, Guid tenantId, Guid tillId, Guid locationId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<StartOperatorSessionCommand, OperatorSessionDto?>>();
        return await handler.HandleAsync(new StartOperatorSessionCommand(userId, tenantId, tillId, locationId, "Caisse partagée · Test"))
            ?? throw new InvalidOperationException("The till session was refused.");
    }

    public Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("verification", email, token);

    public Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("invitation", email, token);

    public Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("reset", email, token);

    private Task Record(string kind, Email email, string token)
    {
        lock (_sent)
        {
            _sent.Add((kind, email.Value, token));
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }
}

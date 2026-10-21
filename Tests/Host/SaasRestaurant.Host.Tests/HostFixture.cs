using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Domain;
using Testcontainers.PostgreSql;

namespace SaasRestaurant.Host.Tests;

/// <summary>
/// Le vrai <c>Program</c> en mode Production, sur un vrai PostgreSQL (Docker). Seule la boîte mail est remplacée,
/// pour lire les liens envoyés par email.
/// </summary>
public sealed class HostFixture : WebApplicationFactory<Program>, IAsyncLifetime, IIdentityNotifier
{
    public const string Password = "correct horse battery";
    public const string AdminEmail = "admin@host-tests.example.com";
    public const string UnclaimedAdminEmail = "unclaimed-admin@host-tests.example.com";

    private static int _sequence;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly List<(string Email, string Token)> _sent = [];

    public Task InitializeAsync() => _container.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:SaasRestaurant", _container.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7)).ToArray()));
        builder.UseSetting("Payments:CredentialsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 11)).ToArray()));
        builder.UseSetting("Notifications:CredentialsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 13)).ToArray()));
        builder.UseSetting("Storefront:TrackingKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7 + 3)).ToArray()));
        builder.UseSetting("Notifications:OtpKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 17)).ToArray()));
        builder.UseSetting("Integrations:SecretsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 19)).ToArray()));
        // Le job des webhooks ne tourne pas : aucun envoi réseau depuis les tests de l'hôte.
        builder.UseSetting("Integrations:RunWorkers", "false");
        builder.UseSetting("Storefront:RunWorkers", "false");
        // Stockage R2 factice : aucun appel n'est fait par ces tests (URLs signées calculées localement).
        builder.UseSetting("Files:R2:AccountId", "host-tests");
        builder.UseSetting("Files:R2:AccessKeyId", "host-tests-key");
        builder.UseSetting("Files:R2:SecretAccessKey", "host-tests-secret");
        builder.UseSetting("Files:R2:Bucket", "host-tests");
        // Tous les clients de test ont la même adresse.
        builder.UseSetting("Identity:RateLimiting:Auth:PermitLimit", "1000000");
        builder.UseSetting("Authorization:PlatformAdminEmails:0", AdminEmail.ToUpperInvariant());
        builder.UseSetting("Authorization:PlatformAdminEmails:1", UnclaimedAdminEmail);
        builder.ConfigureTestServices(services => services.AddSingleton<IIdentityNotifier>(this));
    }

    public static string NewEmail() => $"user{Interlocked.Increment(ref _sequence)}@host-tests.example.com";

    public HttpClient Anonymous() => CreateClient();

    public string LastTokenSentTo(string email)
    {
        lock (_sent)
        {
            return _sent.Last(sent => sent.Email == email).Token;
        }
    }

    /// <summary>Inscrit, vérifie et connecte via HTTP (le compte est réutilisé s'il existe déjà).</summary>
    public async Task<HttpClient> SignedInAsync(string email)
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password = Password });
        if (!login.IsSuccessStatusCode)
        {
            (await client.PostAsJsonAsync("/v1/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("/v1/auth/verify-email", new { token = LastTokenSentTo(email), password = Password })).EnsureSuccessStatusCode();
            login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password = Password });
        }

        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    public Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record(email, token);

    public Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record(email, token);

    public Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record(email, token);

    private Task Record(Email email, string token)
    {
        lock (_sent)
        {
            _sent.Add((email.Value, token));
        }

        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
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
    private readonly ErrorLog _errors = new();

    public Task InitializeAsync() => _container.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:SaasRestaurant", _container.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Identity:AccessTokens:SigningKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7)).ToArray()));
        UseModuleSettings(builder);
        // Tous les clients de test ont la même adresse.
        builder.UseSetting("Identity:RateLimiting:Auth:PermitLimit", "1000000");
        builder.UseSetting("Authorization:PlatformAdminEmails:0", AdminEmail.ToUpperInvariant());
        builder.UseSetting("Authorization:PlatformAdminEmails:1", UnclaimedAdminEmail);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IIdentityNotifier>(this);
            services.AddSingleton<ILoggerProvider>(_errors);
        });
    }

    /// <summary>Clés et réglages exigés en production par les modules (valeurs de test).</summary>
    public static void UseModuleSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Payments:CredentialsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 11)).ToArray()));
        builder.UseSetting("Notifications:CredentialsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 13)).ToArray()));
        builder.UseSetting("Storefront:TrackingKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7 + 3)).ToArray()));
        builder.UseSetting("Notifications:OtpKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 17)).ToArray()));
        builder.UseSetting("Integrations:SecretsKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 19)).ToArray()));
        builder.UseSetting("Fiscal:TokenKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 23)).ToArray()));
        // Le job des webhooks ne tourne pas : aucun envoi réseau depuis les tests de l'hôte.
        builder.UseSetting("Integrations:RunWorkers", "false");
        builder.UseSetting("Storefront:RunWorkers", "false");
        // Stockage R2 factice : aucun appel n'est fait par ces tests (URLs signées calculées localement).
        builder.UseSetting("Files:R2:AccountId", "host-tests");
        builder.UseSetting("Files:R2:AccessKeyId", "host-tests-key");
        builder.UseSetting("Files:R2:SecretAccessKey", "host-tests-secret");
        builder.UseSetting("Files:R2:Bucket", "host-tests");
    }

    /// <summary>Diagnostic d'un test qui attend l'outbox : messages du module qui citent l'id, leurs livraisons, et l'attente.</summary>
    public async Task<string> OutboxStateAsync(string schema, Guid id)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        var lines = new List<string>();
        await using (var pending = new NpgsqlCommand($"SELECT count(*) FROM {schema}.outbox_messages WHERE processed_at_utc IS NULL", connection))
        {
            lines.Add($"{schema} : {await pending.ExecuteScalarAsync()} message(s) non traité(s).");
        }

        await using var command = new NpgsqlCommand(
            $"""
            SELECT m.id, m.type, m.processed_at_utc, m.attempt_count, m.next_attempt_at_utc, m.last_error,
                   (SELECT string_agg(d.consumer || '=' || d.status::text || coalesce(' ' || d.last_error, ''), ' | ')
                    FROM {schema}.outbox_deliveries d WHERE d.message_id = m.id)
            FROM {schema}.outbox_messages m WHERE m.content::text LIKE @id ORDER BY m.id
            """,
            connection);
        command.Parameters.AddWithValue("id", $"%{id}%");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(string.Join(" ; ", Enumerable.Range(0, reader.FieldCount).Select(i => $"{reader.GetName(i)}={(reader.IsDBNull(i) ? "null" : reader.GetValue(i))}")));
        }

        var lifetime = Services.GetRequiredService<IHostApplicationLifetime>();
        lines.Add($"Hôte arrêté : {lifetime.ApplicationStopping.IsCancellationRequested}.");
        lines.AddRange(_errors.Entries.Take(20));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Les erreurs journalisées par l'hôte pendant les tests (diagnostic).</summary>
    private sealed class ErrorLog : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

        public IEnumerable<string> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, System.Collections.Concurrent.ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    entries.Enqueue($"[{logLevel}] {category} : {formatter(state, exception)} {exception?.GetType().Name} {exception?.Message} {exception?.InnerException?.Message}");
                }
            }
        }
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

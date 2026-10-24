using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backups.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Backups.Api;

/// <summary>
/// Le module tel que l'hôte le compose, sur un vrai PostgreSQL (Docker) : vraie migration (index uniques partiels), vraie
/// pré-signature. Simulés : le token, l'horloge, la 2FA (<see cref="ValidCode"/>) et l'agent (SQL direct).
/// </summary>
public sealed class BackupsApi : IAsyncLifetime, IAdminTwoFactor
{
    public const string AdminPolicy = "test-admin";
    public const string Database = "es_restaurant";
    public const string ValidCode = "123456";
    public const string AccessKeyId = "AKIDBACKUPSREAD";
    public const string SecretAccessKey = "backups-read-secret-never-in-url";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public Guid AdminId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<IAdminTwoFactor>(this);
        builder.Services.AddBackupsModule(
            _container.GetConnectionString(),
            settings =>
            {
                settings.DatabaseName = Database;
                settings.KeepMin = 1;
            },
            options =>
            {
                options.RunWorkers = false;
                options.Storage.Endpoint = "https://s3.gra.io.cloud.ovh.net";
                options.Storage.Region = "gra";
                options.Storage.Bucket = "restauspace-backups";
                options.Storage.AccessKeyId = AccessKeyId;
                options.Storage.SecretAccessKey = SecretAccessKey;
            });
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy(AdminPolicy, policy => policy.RequireClaim("role", "admin"));

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapBackupsEndpoints().RequireAuthorization(AdminPolicy);

        await _app.Services.MigrateBackupsDatabaseAsync();
        await _app.StartAsync();
    }

    public IServiceProvider Services => _app.Services;

    public HttpClient Admin() => Client($"admin:{AdminId}");

    /// <summary>Un utilisateur connecté qui n'est pas administrateur de la plateforme.</summary>
    public HttpClient Member() => Client($"member:{Guid.NewGuid()}");

    public HttpClient Anonymous() => _app.GetTestClient();

    /// <summary>Ce que fait l'agent du serveur de base, en SQL direct.</summary>
    public async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    /// <summary>L'agent termine la sauvegarde : clé, taille, empreinte.</summary>
    public Task CompleteAsync(Guid backupId) => SqlAsync(
        "UPDATE backups.backups SET status = 'Completed', storage_key = $1, size_bytes = 2048, checksum_sha256 = $2, started_at_utc = now(), completed_at_utc = now() WHERE id = $3",
        $"{Database}/2026/10/{backupId}.dump.age", new string('c', 64), backupId);

    /// <summary>Remet la file à vide entre deux tests (une seule sauvegarde active à la fois).</summary>
    public Task ResetAsync() => SqlAsync("DELETE FROM backups.restores; DELETE FROM backups.backups; DELETE FROM backups.agent; DELETE FROM backups.outbox_messages;");

    public Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default) =>
        Task.FromResult(userId == AdminId && code == ValidCode);

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }

    private HttpClient Client(string subject)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestToken.NameScheme, subject);
        return client;
    }

    /// <summary>"Authorization: Test admin:{id} | member:{id}".</summary>
    internal sealed class TestToken(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string NameScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith(NameScheme + " ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var (kind, subject) = (header[(NameScheme.Length + 1)..].Split(':')[0], header.Split(':')[^1]);
            List<Claim> claims = [new Claim("sub", subject)];
            if (kind == "admin")
            {
                claims.Add(new Claim("role", "admin"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}

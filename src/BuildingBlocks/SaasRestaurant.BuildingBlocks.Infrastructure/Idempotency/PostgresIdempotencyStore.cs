using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public sealed class PostgresIdempotencyStore(NpgsqlDataSource dataSource, IClock clock) : IIdempotencyStore
{
    public const string Schema = "idempotency";

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            CREATE SCHEMA IF NOT EXISTS {Schema};
            CREATE TABLE IF NOT EXISTS {Schema}.requests (
                scope varchar(400) NOT NULL,
                key varchar(100) NOT NULL,
                request_hash varchar(64) NOT NULL,
                status_code integer NULL,
                content_type varchar(200) NULL,
                body bytea NULL,
                created_at_utc timestamptz NOT NULL,
                expires_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_requests PRIMARY KEY (scope, key)
            );
            CREATE INDEX IF NOT EXISTS ix_requests_expires_at_utc ON {Schema}.requests (expires_at_utc);
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IdempotencyEntry?> TryBeginAsync(
        string scope, string key, string requestHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await using (var expired = dataSource.CreateCommand($"DELETE FROM {Schema}.requests WHERE scope = $1 AND key = $2 AND expires_at_utc <= $3"))
        {
            expired.Parameters.Add(new NpgsqlParameter { Value = scope });
            expired.Parameters.Add(new NpgsqlParameter { Value = key });
            expired.Parameters.Add(new NpgsqlParameter { Value = now });
            await expired.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = dataSource.CreateCommand(
            $"INSERT INTO {Schema}.requests (scope, key, request_hash, created_at_utc, expires_at_utc) VALUES ($1, $2, $3, $4, $5) ON CONFLICT (scope, key) DO NOTHING"))
        {
            insert.Parameters.Add(new NpgsqlParameter { Value = scope });
            insert.Parameters.Add(new NpgsqlParameter { Value = key });
            insert.Parameters.Add(new NpgsqlParameter { Value = requestHash });
            insert.Parameters.Add(new NpgsqlParameter { Value = now });
            insert.Parameters.Add(new NpgsqlParameter { Value = expiresAtUtc });
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return null;
            }
        }

        await using var select = dataSource.CreateCommand($"SELECT request_hash, status_code, content_type, body FROM {Schema}.requests WHERE scope = $1 AND key = $2");
        select.Parameters.Add(new NpgsqlParameter { Value = scope });
        select.Parameters.Add(new NpgsqlParameter { Value = key });
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new IdempotencyEntry(requestHash, null);
        }

        var hash = reader.GetString(0);
        IdempotentResponse? response = await reader.IsDBNullAsync(1, cancellationToken)
            ? null
            : new IdempotentResponse(
                reader.GetInt32(1),
                await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken) ? Array.Empty<byte>() : (byte[])reader.GetValue(3));
        return new IdempotencyEntry(hash, response);
    }

    public async Task CompleteAsync(string scope, string key, IdempotentResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        await using var command = dataSource.CreateCommand(
            $"UPDATE {Schema}.requests SET status_code = $3, content_type = $4, body = $5 WHERE scope = $1 AND key = $2");
        command.Parameters.Add(new NpgsqlParameter { Value = scope });
        command.Parameters.Add(new NpgsqlParameter { Value = key });
        command.Parameters.Add(new NpgsqlParameter { Value = response.StatusCode });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)response.ContentType ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = response.Body });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AbandonAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand($"DELETE FROM {Schema}.requests WHERE scope = $1 AND key = $2 AND status_code IS NULL");
        command.Parameters.Add(new NpgsqlParameter { Value = scope });
        command.Parameters.Add(new NpgsqlParameter { Value = key });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand($"DELETE FROM {Schema}.requests WHERE expires_at_utc <= $1");
        command.Parameters.Add(new NpgsqlParameter { Value = nowUtc });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class IdempotencyPurgeWorker(IIdempotencyStore store, IClock clock, ILogger<IdempotencyPurgeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.PurgeExpiredAsync(clock.UtcNow, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Idempotency purge failed; retrying later.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public static class IdempotencyStoreRegistration
{
    public static IServiceCollection AddPostgresIdempotencyStore(this IServiceCollection services, string connectionString, bool runPurge = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.RemoveAll<IIdempotencyStore>();
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresIdempotencyStore>(provider => new PostgresIdempotencyStore(provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<IClock>()));
        services.AddSingleton<IIdempotencyStore>(provider => provider.GetRequiredService<PostgresIdempotencyStore>());
        if (runPurge)
        {
            services.AddHostedService<IdempotencyPurgeWorker>();
        }

        return services;
    }

    public static async Task EnsureIdempotencyStoreAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.GetService<PostgresIdempotencyStore>() is { } store)
        {
            await store.EnsureCreatedAsync(cancellationToken);
        }
    }
}

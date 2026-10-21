using Npgsql;
using Testcontainers.PostgreSql;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public sealed class PostgresIdempotencyStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly FakeClock _clock = new();
    private NpgsqlDataSource _dataSource = null!;
    private PostgresIdempotencyStore _store = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        _store = new PostgresIdempotencyStore(_dataSource, _clock);
        await _store.EnsureCreatedAsync();
        await _store.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private DateTimeOffset Tomorrow => _clock.UtcNow.AddDays(1);

    [Fact]
    public async Task The_first_request_owns_the_key_and_the_next_ones_see_it_then_its_response()
    {
        Assert.Null(await _store.TryBeginAsync("t|u|POST /payments", "key-1", "hash-a", Tomorrow));

        var inProgress = await _store.TryBeginAsync("t|u|POST /payments", "key-1", "hash-a", Tomorrow);
        Assert.Equal("hash-a", inProgress!.RequestHash);
        Assert.Null(inProgress.Response);

        await _store.CompleteAsync("t|u|POST /payments", "key-1", new IdempotentResponse(201, "application/json", [1, 2, 3]));
        var done = await _store.TryBeginAsync("t|u|POST /payments", "key-1", "hash-b", Tomorrow);

        Assert.Equal("hash-a", done!.RequestHash);
        Assert.Equal((201, "application/json"), (done.Response!.StatusCode, done.Response.ContentType));
        Assert.Equal(new byte[] { 1, 2, 3 }, done.Response.Body);
        Assert.Null(await _store.TryBeginAsync("t|other|POST /payments", "key-1", "hash-a", Tomorrow));
    }

    [Fact]
    public async Task An_abandoned_request_frees_its_key_but_a_completed_one_stays()
    {
        Assert.Null(await _store.TryBeginAsync("scope", "key-2", "hash", Tomorrow));
        await _store.AbandonAsync("scope", "key-2");
        Assert.Null(await _store.TryBeginAsync("scope", "key-2", "hash", Tomorrow));

        await _store.CompleteAsync("scope", "key-2", new IdempotentResponse(204, null, []));
        await _store.AbandonAsync("scope", "key-2");

        Assert.NotNull((await _store.TryBeginAsync("scope", "key-2", "hash", Tomorrow))!.Response);
    }

    [Fact]
    public async Task An_expired_key_can_be_used_again_and_is_purged()
    {
        Assert.Null(await _store.TryBeginAsync("scope", "key-3", "hash", _clock.UtcNow.AddMinutes(1)));
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await _store.TryBeginAsync("scope", "key-3", "other-hash", _clock.UtcNow.AddMinutes(1)));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await _store.PurgeExpiredAsync(_clock.UtcNow) >= 1);
        Assert.Null(await _store.TryBeginAsync("scope", "key-3", "hash", Tomorrow));
    }
}

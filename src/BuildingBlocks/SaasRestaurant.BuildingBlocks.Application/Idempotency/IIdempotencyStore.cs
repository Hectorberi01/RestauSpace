namespace SaasRestaurant.BuildingBlocks.Application;

public sealed record IdempotentResponse(int StatusCode, string? ContentType, byte[] Body);

public sealed record IdempotencyEntry(string RequestHash, IdempotentResponse? Response);

public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> TryBeginAsync(string scope, string key, string requestHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);

    Task CompleteAsync(string scope, string key, IdempotentResponse response, CancellationToken cancellationToken = default);

    Task AbandonAsync(string scope, string key, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

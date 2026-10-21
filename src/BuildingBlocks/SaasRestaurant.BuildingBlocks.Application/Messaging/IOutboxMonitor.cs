namespace SaasRestaurant.BuildingBlocks.Application;

public sealed record OutboxStatus(string Module, int Pending, DateTimeOffset? OldestPendingAtUtc, int DeadLettered);

public sealed record OutboxDeadLetter(
    long MessageId,
    string Type,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset DeadLetteredAtUtc,
    int AttemptCount,
    IReadOnlyList<OutboxFailedDelivery> Deliveries);

public sealed record OutboxFailedDelivery(string Consumer, string Status, int AttemptCount, string? LastError, DateTimeOffset UpdatedAtUtc);

public interface IOutboxMonitor
{
    string Module { get; }

    Task<OutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxDeadLetter>> ListDeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<bool> ReplayAsync(long messageId, CancellationToken cancellationToken = default);
}

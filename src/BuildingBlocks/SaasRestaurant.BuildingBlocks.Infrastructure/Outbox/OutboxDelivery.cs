namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public enum OutboxDeliveryStatus
{
    Delivered = 1,
    Rejected = 2,
    DeadLettered = 3,
}

public sealed class OutboxDelivery
{
    public const int MaxConsumerLength = 200;

    private OutboxDelivery()
    {
        Consumer = null!;
    }

    private OutboxDelivery(long messageId, string consumer, OutboxDeliveryStatus status, int attemptCount, string? lastError, DateTimeOffset updatedAtUtc)
    {
        MessageId = messageId;
        Consumer = consumer;
        Status = status;
        AttemptCount = attemptCount;
        LastError = lastError is null ? null : OutboxMessage.Truncate(lastError);
        UpdatedAtUtc = updatedAtUtc;
    }

    public long MessageId { get; private set; }

    public string Consumer { get; private set; }

    public OutboxDeliveryStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static OutboxDelivery Delivered(long messageId, string consumer, int attemptCount, DateTimeOffset nowUtc) =>
        new(messageId, consumer, OutboxDeliveryStatus.Delivered, attemptCount, null, nowUtc);

    public static OutboxDelivery Rejected(long messageId, string consumer, int attemptCount, string error, DateTimeOffset nowUtc) =>
        new(messageId, consumer, OutboxDeliveryStatus.Rejected, attemptCount, error, nowUtc);

    public static OutboxDelivery DeadLettered(long messageId, string consumer, int attemptCount, string error, DateTimeOffset nowUtc) =>
        new(messageId, consumer, OutboxDeliveryStatus.DeadLettered, attemptCount, error, nowUtc);
}

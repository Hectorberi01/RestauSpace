using System.Reflection;
using System.Text.Json;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>
/// Domain event enregistré dans la transaction qui l'a produit, publié ensuite par un dispatcher.
/// <see cref="Id"/> est attribué par la base et suit l'ordre dans lequel les événements ont été levés.
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 200;

    public const int MaxTraceParentLength = 64;

    private OutboxMessage()
    {
        Type = null!;
        Content = null!;
    }

    private OutboxMessage(string type, int schemaVersion, string content, DateTimeOffset occurredAtUtc, string? traceParent)
    {
        Type = type;
        SchemaVersion = schemaVersion;
        Content = content;
        OccurredAtUtc = occurredAtUtc;
        TraceParent = traceParent;
    }

    public long Id { get; private set; }

    public string Type { get; private set; }

    public int SchemaVersion { get; private set; } = 1;

    public string Content { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }

    public string? TraceParent { get; private set; }

    public static OutboxMessage From(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = domainEvent.GetType();
        return new OutboxMessage(
            type.Name,
            type.GetCustomAttribute<EventVersionAttribute>()?.Version ?? 1,
            JsonSerializer.Serialize(domainEvent, type, OutboxJson.Options),
            domainEvent.OccurredAtUtc,
            OutboxTelemetry.CurrentTraceParent());
    }

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        NextAttemptAtUtc = null;
    }

    public void MarkDeadLettered(DateTimeOffset deadLetteredAtUtc) => DeadLetteredAtUtc ??= deadLetteredAtUtc;

    public void RetryLater(DateTimeOffset nextAttemptAtUtc, string error)
    {
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = Truncate(error);
    }

    public void GiveUp(DateTimeOffset nowUtc, string error)
    {
        AttemptCount++;
        LastError = Truncate(error);
        MarkDeadLettered(nowUtc);
        MarkProcessed(nowUtc);
    }

    public void Replay()
    {
        ProcessedAtUtc = null;
        DeadLetteredAtUtc = null;
        NextAttemptAtUtc = null;
        AttemptCount = 0;
        LastError = null;
    }

    internal static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}

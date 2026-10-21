using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopes, IClock clock, ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 10;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(15);

    public static TimeSpan RetryDelay(int attempt)
    {
        var seconds = FirstRetryDelay.TotalSeconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 20));
        return seconds >= MaxRetryDelay.TotalSeconds ? MaxRetryDelay : TimeSpan.FromSeconds(seconds);
    }

    public static string ConsumerName(IOutboxConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var name = consumer.GetType().FullName ?? consumer.GetType().Name;
        return name.Length <= OutboxDelivery.MaxConsumerLength ? name : name[^OutboxDelivery.MaxConsumerLength..];
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var consumers = scope.ServiceProvider.GetServices<IOutboxConsumer>()
            .Select(consumer => (Name: ConsumerName(consumer), Consumer: consumer))
            .ToList();
        var module = OutboxSql.Module(context);

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await OutboxSql.TryLockAsync(context, module, cancellationToken))
            {
                return 0;
            }

            try
            {
                return await DispatchLockedAsync(context, module, consumers, cancellationToken);
            }
            finally
            {
                await OutboxSql.UnlockAsync(context, module);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> DispatchLockedAsync(
        TContext context, string module, List<(string Name, IOutboxConsumer Consumer)> consumers, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var pending = await context.Set<OutboxMessage>()
            .Where(message => message.ProcessedAtUtc == null && (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now))
            .OrderBy(message => message.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var ids = pending.Select(message => message.Id).ToList();
        var settledByMessage = (await context.Set<OutboxDelivery>()
                .Where(delivery => ids.Contains(delivery.MessageId))
                .Select(delivery => new { delivery.MessageId, delivery.Consumer })
                .ToListAsync(cancellationToken))
            .ToLookup(delivery => delivery.MessageId, delivery => delivery.Consumer);

        foreach (var message in pending)
        {
            var settled = settledByMessage[message.Id].ToHashSet(StringComparer.Ordinal);
            var envelope = new OutboxEnvelope(module, message.Id, message.Type, message.Content, message.OccurredAtUtc, message.SchemaVersion);
            var outcome = new DeliveryOutcome();
            if (message.AttemptCount == 0)
            {
                OutboxTelemetry.FirstDispatch(module, message.Type, clock.UtcNow - message.OccurredAtUtc);
            }

            foreach (var (name, consumer) in consumers.Where(candidate => !settled.Contains(candidate.Name)))
            {
                using var activity = OutboxTelemetry.StartConsumer(module, message, name);
                var started = Stopwatch.GetTimestamp();
                try
                {
                    await consumer.HandleAsync(envelope, cancellationToken);
                    outcome.Succeeded.Add(name);
                    OutboxTelemetry.Delivered(module, name, "delivered", Stopwatch.GetElapsedTime(started));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (OutboxErrors.IsRefusal(exception))
                {
                    var code = OutboxErrors.Code(exception);
                    outcome.Rejected.Add((name, code));
                    activity?.SetStatus(ActivityStatusCode.Error, code);
                    OutboxTelemetry.Delivered(module, name, "rejected", Stopwatch.GetElapsedTime(started));
                    logger.LogWarning(
                        "Outbox message {Module}/{MessageId} ({Type}) rejected by {Consumer}: {Code}.", module, message.Id, message.Type, name, code);
                }
                catch (Exception exception)
                {
                    var code = OutboxErrors.Code(exception);
                    outcome.Failed.Add((name, code));
                    activity?.SetStatus(ActivityStatusCode.Error, code);
                    OutboxTelemetry.Delivered(module, name, "failed", Stopwatch.GetElapsedTime(started));
                    logger.LogError(
                        exception, "Outbox message {Module}/{MessageId} ({Type}) failed in {Consumer}: {Code}.", module, message.Id, message.Type, name, code);
                }
            }

            Record(context, module, message, outcome, clock.UtcNow);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        return pending.Count;
    }

    private static void Record(TContext context, string module, OutboxMessage message, DeliveryOutcome outcome, DateTimeOffset now)
    {
        if (outcome.Rejected.Count == 0 && outcome.Failed.Count == 0)
        {
            message.MarkProcessed(now);
            return;
        }

        var deliveries = context.Set<OutboxDelivery>();
        var attempt = message.AttemptCount + 1;
        deliveries.AddRange(outcome.Succeeded.Select(name => OutboxDelivery.Delivered(message.Id, name, attempt, now)));
        deliveries.AddRange(outcome.Rejected.Select(rejected => OutboxDelivery.Rejected(message.Id, rejected.Name, attempt, rejected.Code, now)));
        if (outcome.Rejected.Count > 0 && message.DeadLetteredAtUtc is null)
        {
            message.MarkDeadLettered(now);
            OutboxTelemetry.MovedToDeadLetter(module, message.Type);
        }

        if (outcome.Failed.Count == 0)
        {
            message.MarkProcessed(now);
            return;
        }

        var error = outcome.Failed[0].Code;
        if (attempt >= MaxAttempts)
        {
            deliveries.AddRange(outcome.Failed.Select(failed => OutboxDelivery.DeadLettered(message.Id, failed.Name, attempt, failed.Code, now)));
            if (message.DeadLetteredAtUtc is null)
            {
                OutboxTelemetry.MovedToDeadLetter(module, message.Type);
            }

            message.GiveUp(now, error);
            return;
        }

        message.RetryLater(now + RetryDelay(attempt), error);
    }

    public async Task<int> DeleteOldMessagesAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var limit = clock.UtcNow - Retention;
        return await scope.ServiceProvider.GetRequiredService<TContext>().Set<OutboxMessage>()
            .Where(message => message.ProcessedAtUtc != null && message.ProcessedAtUtc < limit && message.DeadLetteredAtUtc == null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchPendingAsync(stoppingToken) == BatchSize)
                {
                }

                if (DateTimeOffset.UtcNow - lastCleanup > CleanupInterval)
                {
                    lastCleanup = DateTimeOffset.UtcNow;
                    await DeleteOldMessagesAsync(stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Outbox dispatch failed for {Context}; retrying.", typeof(TContext).Name);
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private sealed class DeliveryOutcome
    {
        public List<string> Succeeded { get; } = [];

        public List<(string Name, string Code)> Rejected { get; } = [];

        public List<(string Name, string Code)> Failed { get; } = [];
    }
}

public static class OutboxDispatcherRegistration
{
    public static IServiceCollection AddOutboxDispatcher<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddSingleton<OutboxDispatcher<TContext>>();
        services.AddHostedService(provider => provider.GetRequiredService<OutboxDispatcher<TContext>>());
        services.AddSingleton<IOutboxMonitor, OutboxMonitor<TContext>>();
        return services;
    }
}

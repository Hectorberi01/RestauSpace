using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

public sealed class OutboxMonitor<TContext>(IServiceScopeFactory scopes) : IOutboxMonitor
    where TContext : DbContext
{
    public const int MaxPageSize = 100;

    private string? _module;

    public string Module => _module ??= ReadModule();

    public async Task<OutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var messages = scope.ServiceProvider.GetRequiredService<TContext>().Set<OutboxMessage>().AsNoTracking();
        var pending = await messages.CountAsync(message => message.ProcessedAtUtc == null, cancellationToken);
        var oldest = await messages
            .Where(message => message.ProcessedAtUtc == null)
            .OrderBy(message => message.Id)
            .Select(message => (DateTimeOffset?)message.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var deadLettered = await messages.CountAsync(message => message.DeadLetteredAtUtc != null, cancellationToken);
        return new OutboxStatus(Module, pending, oldest, deadLettered);
    }

    public async Task<IReadOnlyList<OutboxDeadLetter>> ListDeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (Math.Max(page, 1) - 1) * size;
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var messages = await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.DeadLetteredAtUtc != null)
            .OrderByDescending(message => message.Id)
            .Skip(skip)
            .Take(size)
            .ToListAsync(cancellationToken);
        var ids = messages.Select(message => message.Id).ToList();
        var deliveries = await context.Set<OutboxDelivery>().AsNoTracking()
            .Where(delivery => ids.Contains(delivery.MessageId) && delivery.Status != OutboxDeliveryStatus.Delivered)
            .ToListAsync(cancellationToken);
        return messages
            .Select(message => new OutboxDeadLetter(
                message.Id,
                message.Type,
                message.OccurredAtUtc,
                message.DeadLetteredAtUtc!.Value,
                message.AttemptCount,
                deliveries
                    .Where(delivery => delivery.MessageId == message.Id)
                    .OrderBy(delivery => delivery.Consumer, StringComparer.Ordinal)
                    .Select(delivery => new OutboxFailedDelivery(
                        delivery.Consumer, delivery.Status.ToString(), delivery.AttemptCount, delivery.LastError, delivery.UpdatedAtUtc))
                    .ToList()))
            .ToList();
    }

    public async Task<bool> ReplayAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var message = await context.Set<OutboxMessage>()
            .SingleOrDefaultAsync(candidate => candidate.Id == messageId && candidate.DeadLetteredAtUtc != null, cancellationToken);
        if (message is null)
        {
            return false;
        }

        var failed = await context.Set<OutboxDelivery>()
            .Where(delivery => delivery.MessageId == messageId && delivery.Status != OutboxDeliveryStatus.Delivered)
            .ToListAsync(cancellationToken);
        context.Set<OutboxDelivery>().RemoveRange(failed);
        message.Replay();
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private string ReadModule()
    {
        using var scope = scopes.CreateScope();
        return OutboxSql.Module(scope.ServiceProvider.GetRequiredService<TContext>());
    }
}

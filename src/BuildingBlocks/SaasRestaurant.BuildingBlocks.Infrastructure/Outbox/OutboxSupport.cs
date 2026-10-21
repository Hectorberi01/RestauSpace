namespace SaasRestaurant.BuildingBlocks.Infrastructure;

internal static class OutboxErrors
{
    public static bool IsRefusal(Exception exception) =>
        exception is DomainException or NotFoundException or ForbiddenException;

    public static string Code(Exception exception) => exception switch
    {
        DomainException domain => domain.Code,
        NotFoundException notFound => notFound.Error.Code,
        ForbiddenException forbidden => forbidden.Error.Code,
        ConcurrencyException concurrency => concurrency.Error.Code,
        _ => exception.GetType().Name,
    };
}

internal static class OutboxSql
{
    public static string Module(DbContext context) => context.Model.GetDefaultSchema() ?? "public";

    public static async Task<bool> TryLockAsync(DbContext context, string module, CancellationToken cancellationToken)
    {
        var key = "outbox:" + module;
        return await context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({key}, 0)) AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public static async Task UnlockAsync(DbContext context, string module)
    {
        var key = "outbox:" + module;
        await context.Database
            .SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({key}, 0)) AS \"Value\"")
            .SingleAsync(CancellationToken.None);
    }
}

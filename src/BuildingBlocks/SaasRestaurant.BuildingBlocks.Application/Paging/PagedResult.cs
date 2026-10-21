namespace SaasRestaurant.BuildingBlocks.Application;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int? TotalCount, bool HasMore)
{
    public static PagedResult<T> Counted(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount, (long)page * pageSize < totalCount);

    public static PagedResult<T> Uncounted(IReadOnlyList<T> items, int page, int pageSize) =>
        new(items, page, pageSize, null, items.Count >= pageSize);
}

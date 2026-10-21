namespace SaasRestaurant.BuildingBlocks.Api;

public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    public static readonly DomainError Invalid =
        new("paging.invalid", $"page must be at least 1 and pageSize between 1 and {MaxPageSize}.");

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest From(int? page, int? pageSize)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? DefaultPageSize);
        DomainException.ThrowIf(request.Page < 1 || request.PageSize is < 1 or > MaxPageSize || request.Page > int.MaxValue / MaxPageSize, Invalid);
        return request;
    }

    public PagedResult<T> Of<T>(IReadOnlyList<T> items) => PagedResult<T>.Uncounted(items, Page, PageSize);
}

public static class PageRequestExtensions
{
    public static async Task<PagedResult<T>> LoadAsync<T>(this PageRequest request, Func<int, int, Task<IReadOnlyList<T>>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return request.Of(await load(request.Skip, request.PageSize));
    }
}

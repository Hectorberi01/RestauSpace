namespace SaasRestaurant.BuildingBlocks.Api;

public sealed class PagingTests
{
    [Fact]
    public void Defaults_to_the_first_page()
    {
        var request = PageRequest.From(null, null);

        Assert.Equal((1, PageRequest.DefaultPageSize, 0), (request.Page, request.PageSize, request.Skip));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Rejects_out_of_range_values(int page, int pageSize)
    {
        var refused = Assert.Throws<DomainException>(() => PageRequest.From(page, pageSize));

        Assert.Equal(PageRequest.Invalid, refused.Error);
    }

    [Fact]
    public async Task Loads_the_requested_slice_and_reports_whether_more_may_follow()
    {
        var source = Enumerable.Range(1, 25).ToList();
        Task<IReadOnlyList<int>> Load(int skip, int take) => Task.FromResult<IReadOnlyList<int>>(source.Skip(skip).Take(take).ToList());

        var full = await PageRequest.From(2, 10).LoadAsync(Load);
        var last = await PageRequest.From(3, 10).LoadAsync(Load);

        Assert.Equal((11, 2, 10, true), (full.Items[0], full.Page, full.PageSize, full.HasMore));
        Assert.Equal((5, false), (last.Items.Count, last.HasMore));
        Assert.Null(last.TotalCount);
    }

    [Fact]
    public void A_counted_page_knows_whether_it_is_the_last()
    {
        Assert.True(PagedResult<int>.Counted([1, 2], 1, 2, 3).HasMore);
        Assert.False(PagedResult<int>.Counted([3], 2, 2, 3).HasMore);
    }
}

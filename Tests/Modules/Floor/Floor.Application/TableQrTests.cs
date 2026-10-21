using Floor.Application.Commands;

namespace Floor.Application;

public sealed class TableQrTests
{
    private readonly FloorFixture _fixture = new();

    [Fact]
    public async Task Each_table_gets_a_random_qr_token_that_finds_it_back_and_rotation_retires_the_old_one()
    {
        var (area, table) = await _fixture.GivenTableAsync();
        await _fixture.AddTable.HandleAsync(new AddTableCommand(area, "T2", 2));
        var location = _fixture.Areas.Stored(area).LocationId.Value;

        var codes = await new PrepareTableQrCodesHandler(_fixture.Workspace).HandleAsync(new PrepareTableQrCodesCommand(location));
        var again = await new PrepareTableQrCodesHandler(_fixture.Workspace).HandleAsync(new PrepareTableQrCodesCommand(location));
        var first = codes.Single(code => code.TableId == table);
        var found = await new FindTableByQrTokenHandler(_fixture.Workspace).HandleAsync(new FindTableByQrTokenQuery(first.Token));
        var rotated = await new RotateTableQrTokenHandler(_fixture.Workspace).HandleAsync(new RotateTableQrTokenCommand(table));
        var old = await new FindTableByQrTokenHandler(_fixture.Workspace).HandleAsync(new FindTableByQrTokenQuery(first.Token));

        Assert.Equal(2, codes.Select(code => code.Token).Distinct().Count());
        Assert.Equal(codes.Select(code => code.Token), again.Select(code => code.Token));
        Assert.DoesNotContain("T1", first.Token);
        Assert.Equal(("T1", location), (found!.Label, found.LocationId));
        Assert.NotEqual(first.Token, rotated.Token);
        Assert.Null(old);
        Assert.Null(await new FindTableByQrTokenHandler(_fixture.Workspace).HandleAsync(new FindTableByQrTokenQuery("../etc")));
    }
}

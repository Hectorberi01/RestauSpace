using Orders.Application.Commands;
using Orders.Domain;

namespace Orders.Application;

public sealed class OrderRecomposeAndMoveTests
{
    private readonly OrdersFixture _fixture = new();

    [Fact]
    public async Task A_draft_line_gets_new_options_without_being_removed()
    {
        var (order, item) = await _fixture.GivenDraftAsync();

        var replaced = await new ReplaceOrderItemHandler(_fixture.Workspace, _fixture)
            .HandleAsync(new ReplaceOrderItemCommand(order, item, 3, null, [Guid.NewGuid()], "sans piment"));

        var line = Assert.Single(_fixture.Orders.Stored(order).Items);
        Assert.Equal((replaced, 3, "sans piment", 1), (line.Id.Value, line.Quantity.Value, line.Note, line.Modifiers.Count));
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task Merging_tables_moves_every_open_order_to_the_other_session()
    {
        var from = _fixture.GivenSession();
        var into = _fixture.GivenSession();
        var (first, _) = await _fixture.GivenDraftAsync(from);
        var (second, _) = await _fixture.GivenDraftAsync(from);
        var (stays, _) = await _fixture.GivenDraftAsync(into);

        var moved = await new MoveTableSessionOrdersHandler(_fixture.Workspace, _fixture, _fixture.Clock)
            .HandleAsync(new MoveTableSessionOrdersCommand(from, into));

        Assert.Equal(2, moved);
        Assert.All(new[] { first, second, stays }, id => Assert.Equal(into, _fixture.Orders.Stored(id).TableSessionId!.Value.Value));
    }

    [Fact]
    public async Task One_order_changes_table_only_to_an_open_session_of_this_restaurant()
    {
        var (order, _) = await _fixture.GivenDraftAsync(_fixture.GivenSession());
        var handler = new MoveOrderToTableSessionHandler(_fixture.Workspace, _fixture, _fixture.Clock);
        var target = _fixture.GivenSession();

        await handler.HandleAsync(new MoveOrderToTableSessionCommand(order, target));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(new MoveOrderToTableSessionCommand(order, Guid.NewGuid())));

        Assert.Equal(target, _fixture.Orders.Stored(order).TableSessionId!.Value.Value);
        Assert.Equal(OrdersApplicationErrors.TableSessionNotFound, unknown.Error);
    }
}

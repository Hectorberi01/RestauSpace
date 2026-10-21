using Floor.Application.Abstractions;
using Floor.Application.Commands;
using Floor.Domain;

namespace Floor.Application;

public sealed class TableMoveMergeUseCaseTests : ITableSessionOrdersMover
{
    private readonly FloorFixture _fixture = new();
    private readonly List<(TableSessionId From, TableSessionId To)> _moves = [];

    Task ITableSessionOrdersMover.MoveOpenOrdersAsync(TenantId tenantId, TableSessionId from, TableSessionId to, CancellationToken cancellationToken)
    {
        _moves.Add((from, to));
        return Task.CompletedTask;
    }

    private async Task<Guid> SeatAsync(Guid table, int guests) =>
        await _fixture.OpenSession.HandleAsync(new OpenTableSessionCommand(table, guests));

    [Fact]
    public async Task Guests_change_table_and_a_busy_table_is_refused()
    {
        var (area, t1) = await _fixture.GivenTableAsync();
        var t2 = await _fixture.AddTable.HandleAsync(new AddTableCommand(area, "T2", 4));
        var t3 = await _fixture.AddTable.HandleAsync(new AddTableCommand(area, "T3", 4));
        var session = await SeatAsync(t1, 2);
        await SeatAsync(t3, 2);
        var move = new MoveTableSessionHandler(_fixture.Workspace, _fixture.Sessions, _fixture.Clock);

        await move.HandleAsync(new MoveTableSessionCommand(session, t2));
        var busy = await Assert.ThrowsAsync<DomainException>(() => move.HandleAsync(new MoveTableSessionCommand(session, t3)));

        Assert.Equal(t2, (await _fixture.GetSession.HandleAsync(new Queries.GetTableSessionByIdQuery(session))).TableId);
        Assert.Equal(TableSessionErrors.TableAlreadyOccupied, busy.Error);
    }

    [Fact]
    public async Task Merging_moves_the_orders_first_then_closes_the_merged_table()
    {
        var (area, t1) = await _fixture.GivenTableAsync();
        var t2 = await _fixture.AddTable.HandleAsync(new AddTableCommand(area, "T2", 4));
        var into = await SeatAsync(t1, 2);
        var from = await SeatAsync(t2, 3);

        await new MergeTableSessionsHandler(_fixture.Workspace, this, _fixture.Clock).HandleAsync(new MergeTableSessionsCommand(from, into));

        Assert.Equal((TableSessionId.From(from), TableSessionId.From(into)), Assert.Single(_moves));
        Assert.Null((await _fixture.ListOpenSessions.HandleAsync(new Queries.ListOpenTableSessionsQuery(null))).SingleOrDefault(session => session.Id == from));
        Assert.Equal(5, (await _fixture.GetSession.HandleAsync(new Queries.GetTableSessionByIdQuery(into))).GuestCount);
    }
}

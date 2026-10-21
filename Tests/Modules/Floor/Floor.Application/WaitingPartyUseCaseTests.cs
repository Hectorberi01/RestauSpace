using Floor.Application.Abstractions;
using Floor.Application.Commands;
using Floor.Domain;

namespace Floor.Application;

public sealed class WaitingPartyUseCaseTests
{
    private readonly FloorFixture _fixture = new();
    private readonly InMemoryParties _parties = new();

    private AddWaitingPartyHandler Add => new(_fixture.Workspace, _fixture, _parties, _fixture.Clock);

    private NotifyWaitingPartyHandler Notify => new(_fixture.Workspace, _parties, _fixture.Clock);

    private SeatWaitingPartyHandler Seat => new(_fixture.Workspace, _parties, _fixture.Sessions, _fixture.Clock);

    private RemoveWaitingPartyHandler Remove => new(_fixture.Workspace, _parties, _fixture.Clock);

    private ListWaitingPartiesHandler List => new(_fixture.Workspace, _parties, _fixture.Clock);

    [Fact]
    public async Task Parties_queue_in_arrival_order_and_are_told_when_their_table_is_ready()
    {
        var location = _fixture.GivenLocation();
        var first = await Add.HandleAsync(new AddWaitingPartyCommand(location, " Awa ", 2, "+22990000001", QuotedMinutes: 15));
        var second = await Add.HandleAsync(new AddWaitingPartyCommand(location, "Kofi", 4));

        await Notify.HandleAsync(new NotifyWaitingPartyCommand(first));
        var noPhone = await Assert.ThrowsAsync<DomainException>(() => Notify.HandleAsync(new NotifyWaitingPartyCommand(second)));

        var list = await List.HandleAsync(new ListWaitingPartiesQuery(location));
        Assert.Equal([("Awa", 1, WaitingPartyStatus.Notified), ("Kofi", 2, WaitingPartyStatus.Waiting)], list.Select(party => (party.Name, party.Position!.Value, party.Status)));
        Assert.Equal(WaitingPartyErrors.NoPhone, noPhone.Error);
        DomainAssert.Raised<WaitingPartyNotified>(_parties.Items[0]);
    }

    [Fact]
    public async Task Seating_opens_a_session_and_forgets_the_phone()
    {
        var (_, table) = await _fixture.GivenTableAsync();
        var location = _fixture.Locations.Single().LocationId.Value;
        var party = await Add.HandleAsync(new AddWaitingPartyCommand(location, "Awa", 3, "+22990000001"));

        var session = await Seat.HandleAsync(new SeatWaitingPartyCommand(party, table));

        var stored = _parties.Items.Single();
        Assert.Equal((WaitingPartyStatus.Seated, (PhoneNumber?)null), (stored.Status, stored.Phone));
        Assert.Equal(session, stored.TableSessionId!.Value.Value);
        Assert.Equal(3, _fixture.Sessions.Items.Single().GuestCount);
        var twice = await Assert.ThrowsAsync<DomainException>(() => Notify.HandleAsync(new NotifyWaitingPartyCommand(party)));
        Assert.Equal(WaitingPartyErrors.Closed, twice.Error);
    }

    [Fact]
    public async Task A_removed_party_leaves_the_queue_and_old_ones_are_purged()
    {
        var location = _fixture.GivenLocation();
        var party = await Add.HandleAsync(new AddWaitingPartyCommand(location, "Awa", 2, "+22990000001"));
        await Remove.HandleAsync(new RemoveWaitingPartyCommand(party));
        Assert.Equal(WaitingPartyStatus.Left, _parties.Items.Single().Status);

        _fixture.Clock.Advance(TimeSpan.FromHours(13));
        await Add.HandleAsync(new AddWaitingPartyCommand(location, "Kofi", 2));

        Assert.Equal(["Kofi"], _parties.Items.Select(item => item.Name));
    }

    private sealed class InMemoryParties : IWaitingPartyRepository
    {
        public List<WaitingParty> Items { get; } = [];

        public Task<WaitingParty?> FindAsync(TenantId tenantId, WaitingPartyId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(party => party.TenantId == tenantId && party.Id == id));

        public Task<IReadOnlyList<WaitingParty>> ListAsync(TenantId tenantId, LocationId locationId, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitingParty>>(Items
                .Where(party => party.TenantId == tenantId && party.LocationId == locationId && (party.IsOpen || party.ClosedAtUtc >= closedSinceUtc))
                .OrderBy(party => party.CreatedAtUtc)
                .ToList());

        public Task<int> PurgeClosedAsync(TenantId tenantId, LocationId locationId, DateTimeOffset beforeUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.RemoveAll(party => party.TenantId == tenantId && party.LocationId == locationId && party.ClosedAtUtc < beforeUtc));

        public void Add(WaitingParty party) => Items.Add(party);
    }
}

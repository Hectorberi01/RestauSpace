namespace Floor.Domain;

public sealed class TableMoveMergeTests
{
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();
    private DiningArea? _area;

    private TableReference NewTable(string label, bool isInService = true, LocationId? location = null)
    {
        if (location is { } other)
        {
            var elsewhere = DiningArea.Create(_tenantId, new LocationReference(_tenantId, other, IsOperational: true), "Terrasse", _clock);
            return elsewhere.GetTableReference(elsewhere.AddTable(label, 4, _clock));
        }

        _area ??= DiningArea.Create(_tenantId, new LocationReference(_tenantId, _locationId, IsOperational: true), "Salle", _clock);
        var id = _area.AddTable(label, 4, _clock);
        if (!isInService)
        {
            _area.SetTableOutOfService(id, _clock);
        }

        return _area.GetTableReference(id);
    }

    private TableSession Seat(TableReference table, int guests) =>
        TableSession.Open(_tenantId, table, guests, tableHasOpenSession: false, _clock);

    [Fact]
    public void Guests_move_to_a_free_table_of_the_same_location()
    {
        var session = Seat(NewTable("T1"), 2);
        var t2 = NewTable("T2");
        session.ClearDomainEvents();

        session.MoveTo(t2, targetHasOpenSession: false, _clock);

        Assert.Equal(t2.TableId, session.TableId);
        Assert.Single(session.DomainEvents.OfType<TableSessionMoved>());
        Assert.Equal(TableSessionErrors.TableAlreadyOccupied, Assert.Throws<DomainException>(() => session.MoveTo(NewTable("T3"), true, _clock)).Error);
        Assert.Equal(TableSessionErrors.TableOutOfService, Assert.Throws<DomainException>(() => session.MoveTo(NewTable("T4", isInService: false), false, _clock)).Error);
        Assert.Equal(TableSessionErrors.OtherLocation, Assert.Throws<DomainException>(() => session.MoveTo(NewTable("X1", location: LocationId.New()), false, _clock)).Error);
    }

    [Fact]
    public void Merging_adds_the_guests_to_the_other_table_and_closes_the_session()
    {
        var target = Seat(NewTable("T1"), 2);
        var source = Seat(NewTable("T2"), 3);
        source.ClearDomainEvents();

        source.MergeInto(target, _clock);

        Assert.False(source.IsOpen);
        Assert.Equal(5, target.GuestCount);
        Assert.Single(source.DomainEvents.OfType<TableSessionMerged>());
        Assert.Single(source.DomainEvents.OfType<TableSessionClosed>());
        Assert.Equal(TableSessionErrors.MergeWithItself, Assert.Throws<DomainException>(() => target.MergeInto(target, _clock)).Error);
        Assert.Equal(TableSessionErrors.ClosedIsImmutable, Assert.Throws<DomainException>(() => source.MergeInto(target, _clock)).Error);
    }
}

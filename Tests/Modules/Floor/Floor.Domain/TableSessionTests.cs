namespace Floor.Domain;

public sealed class TableSessionTests
{
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();

    // Une TableReference ne se construit pas à la main : elle provient toujours d'une vraie salle.
    private TableReference Table(bool isInService = true) => TableOf(_tenantId, _locationId, isInService);

    private TableReference TableOf(TenantId tenantId, LocationId locationId, bool isInService = true)
    {
        var area = DiningArea.Create(tenantId, new LocationReference(tenantId, locationId, IsOperational: true), "Salle", _clock);
        var tableId = area.AddTable("T1", 4, _clock);
        if (!isInService)
        {
            area.SetTableOutOfService(tableId, _clock);
        }

        return area.GetTableReference(tableId);
    }

    private TableSession OpenSession(int guestCount = 2)
    {
        var session = TableSession.Open(_tenantId, Table(), guestCount, tableHasOpenSession: false, _clock);
        session.ClearDomainEvents();
        return session;
    }

    private TableSession ClosedSession()
    {
        var session = OpenSession();
        _clock.Advance(TimeSpan.FromHours(1));
        session.Close(hasOpenOrders: false, _clock);
        session.ClearDomainEvents();
        return session;
    }

    [Fact]
    public void Open_starts_a_session_on_the_table_and_raises_TableSessionOpened()
    {
        var table = Table();

        var session = TableSession.Open(_tenantId, table, 3, tableHasOpenSession: false, _clock);

        Assert.Equal(_tenantId, session.TenantId);
        Assert.Equal(_locationId, session.LocationId);
        Assert.Equal(table.TableId, session.TableId);
        Assert.Equal(3, session.GuestCount);
        Assert.Null(session.Reservation);
        Assert.True(session.IsWalkIn);
        Assert.Equal(_clock.UtcNow, session.OpenedAtUtc);
        Assert.Null(session.ClosedAtUtc);
        Assert.True(session.IsOpen);
        var raised = DomainAssert.Raised<TableSessionOpened>(session);
        Assert.Equal(new TableSessionOpened(_tenantId, session.Id, _locationId, table.TableId, 3, null, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Open_rejects_a_guest_count_that_is_not_positive(int guestCount)
    {
        DomainAssert.Throws(
            TableSessionErrors.GuestCountNotPositive,
            () => TableSession.Open(_tenantId, Table(), guestCount, tableHasOpenSession: false, _clock));
    }

    [Fact]
    public void Open_accepts_a_single_guest()
    {
        Assert.Equal(1, OpenSession(guestCount: 1).GuestCount);
    }

    [Fact]
    public void Open_rejects_an_out_of_service_table()
    {
        DomainAssert.Throws(
            TableSessionErrors.TableOutOfService,
            () => TableSession.Open(_tenantId, Table(isInService: false), 2, tableHasOpenSession: false, _clock));
    }

    [Fact]
    public void Open_rejects_a_table_that_already_has_an_open_session()
    {
        DomainAssert.Throws(
            TableSessionErrors.TableAlreadyOccupied,
            () => TableSession.Open(_tenantId, Table(), 2, tableHasOpenSession: true, _clock));
    }

    [Fact]
    public void Open_rejects_a_table_of_another_tenant()
    {
        var foreignTable = TableOf(TenantId.New(), _locationId);

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => TableSession.Open(_tenantId, foreignTable, 2, tableHasOpenSession: false, _clock));
    }

    [Fact]
    public void Open_uses_the_reference_given_by_the_dining_area()
    {
        var areaLocation = LocationId.New();
        var area = DiningArea.Create(_tenantId, new LocationReference(_tenantId, areaLocation, IsOperational: true), "Salle", _clock);
        var tableId = area.AddTable("T1", 4, _clock);

        var session = TableSession.Open(_tenantId, area.GetTableReference(tableId), 4, tableHasOpenSession: false, _clock);
        Assert.Equal(tableId, session.TableId);
        Assert.Equal(areaLocation, session.LocationId);
        Assert.Equal(areaLocation, DomainAssert.Raised<TableSessionOpened>(session).LocationId);

        area.SetTableOutOfService(tableId, _clock);
        DomainAssert.Throws(
            TableSessionErrors.TableOutOfService,
            () => TableSession.Open(_tenantId, area.GetTableReference(tableId), 4, tableHasOpenSession: false, _clock));
    }

    [Fact]
    public void A_second_session_on_the_same_table_is_refused_while_the_first_is_open_then_accepted_once_closed()
    {
        var table = Table();
        var first = TableSession.Open(_tenantId, table, 2, tableHasOpenSession: false, _clock);

        DomainAssert.Throws(
            TableSessionErrors.TableAlreadyOccupied,
            () => TableSession.Open(_tenantId, table, 2, tableHasOpenSession: first.IsOpen, _clock));

        first.Close(hasOpenOrders: false, _clock);
        var second = TableSession.Open(_tenantId, table, 2, tableHasOpenSession: first.IsOpen, _clock);

        Assert.True(second.IsOpen);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ChangeGuestCount_updates_the_count_and_raises_TableSessionGuestCountChanged()
    {
        var session = OpenSession(guestCount: 2);

        session.ChangeGuestCount(5, _clock);

        Assert.Equal(5, session.GuestCount);
        var raised = DomainAssert.Raised<TableSessionGuestCountChanged>(session);
        Assert.Equal(
            new TableSessionGuestCountChanged(_tenantId, session.Id, _locationId, session.TableId, 2, 5, _clock.UtcNow),
            raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ChangeGuestCount_rejects_a_count_that_is_not_positive(int guestCount)
    {
        var session = OpenSession(guestCount: 2);

        DomainAssert.Throws(TableSessionErrors.GuestCountNotPositive, () => session.ChangeGuestCount(guestCount, _clock));

        Assert.Equal(2, session.GuestCount);
        DomainAssert.NoEvents(session);
    }

    [Fact]
    public void ChangeGuestCount_to_the_same_value_raises_nothing()
    {
        var session = OpenSession(guestCount: 2);

        session.ChangeGuestCount(2, _clock);

        DomainAssert.NoEvents(session);
    }

    [Fact]
    public void Close_ends_the_session_and_raises_TableSessionClosed()
    {
        var session = OpenSession(guestCount: 3);
        var openedAt = _clock.UtcNow;
        _clock.Advance(TimeSpan.FromMinutes(90));

        session.Close(hasOpenOrders: false, _clock);

        Assert.False(session.IsOpen);
        Assert.Equal(_clock.UtcNow, session.ClosedAtUtc);
        var raised = DomainAssert.Raised<TableSessionClosed>(session);
        Assert.Equal(
            new TableSessionClosed(_tenantId, session.Id, _locationId, session.TableId, 3, null, openedAt, _clock.UtcNow),
            raised);
    }

    [Fact]
    public void Close_rejects_a_session_that_still_has_open_orders()
    {
        var session = OpenSession();

        DomainAssert.Throws(TableSessionErrors.HasOpenOrders, () => session.Close(hasOpenOrders: true, _clock));

        Assert.True(session.IsOpen);
        Assert.Null(session.ClosedAtUtc);
        DomainAssert.NoEvents(session);
    }

    [Fact]
    public void Close_is_accepted_once_the_last_order_is_no_longer_open()
    {
        var session = OpenSession();
        DomainAssert.Throws(TableSessionErrors.HasOpenOrders, () => session.Close(hasOpenOrders: true, _clock));

        session.Close(hasOpenOrders: false, _clock);

        Assert.False(session.IsOpen);
        DomainAssert.Raised<TableSessionClosed>(session);
    }

    [Fact]
    public void Close_reports_an_already_closed_session_before_looking_at_its_orders()
    {
        var session = ClosedSession();

        DomainAssert.Throws(TableSessionErrors.AlreadyClosed, () => session.Close(hasOpenOrders: true, _clock));
    }

    [Fact]
    public void Open_for_a_reservation_keeps_its_reference_and_publishes_it()
    {
        var reservation = ReservationReference.From(Guid.NewGuid());
        var table = Table();

        var session = TableSession.Open(_tenantId, table, 4, tableHasOpenSession: false, _clock, reservation);

        Assert.Equal(reservation, session.Reservation);
        Assert.False(session.IsWalkIn);
        Assert.Equal(
            new TableSessionOpened(_tenantId, session.Id, _locationId, table.TableId, 4, reservation, _clock.UtcNow),
            DomainAssert.Raised<TableSessionOpened>(session));
    }

    [Fact]
    public void Close_of_a_reserved_session_publishes_the_reservation_reference()
    {
        var reservation = ReservationReference.From(Guid.NewGuid());
        var session = TableSession.Open(_tenantId, Table(), 4, tableHasOpenSession: false, _clock, reservation);
        var openedAt = _clock.UtcNow;
        _clock.Advance(TimeSpan.FromMinutes(75));

        session.Close(hasOpenOrders: false, _clock);

        Assert.Equal(
            new TableSessionClosed(_tenantId, session.Id, _locationId, session.TableId, 4, reservation, openedAt, _clock.UtcNow),
            DomainAssert.Raised<TableSessionClosed>(session));
    }

    [Fact]
    public void Open_rejects_an_empty_tenant_or_an_empty_reservation_reference()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => TableSession.Open(default, Table(), 2, tableHasOpenSession: false, _clock));
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => TableSession.Open(_tenantId, Table(), 2, tableHasOpenSession: false, _clock, default(ReservationReference)));
    }

    [Fact]
    public void TableReference_cannot_be_built_nor_altered_outside_the_dining_area()
    {
        Assert.Empty(typeof(TableReference).GetConstructors());
        Assert.All(typeof(TableReference).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void ReservationReference_rejects_an_empty_id_and_compares_by_value()
    {
        var id = Guid.NewGuid();

        DomainAssert.Throws(CommonErrors.EmptyId, () => ReservationReference.From(Guid.Empty));
        Assert.Equal(ReservationReference.From(id), ReservationReference.From(id));
        Assert.Equal(id, ReservationReference.From(id).Value);
    }

    [Fact]
    public void Close_rejects_a_second_close_and_keeps_the_first_closing_time()
    {
        var session = ClosedSession();
        var closedAt = session.ClosedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(5));

        DomainAssert.Throws(TableSessionErrors.AlreadyClosed, () => session.Close(hasOpenOrders: false, _clock));

        Assert.Equal(closedAt, session.ClosedAtUtc);
        DomainAssert.NoEvents(session);
    }

    [Fact]
    public void A_closed_session_cannot_change_its_guest_count()
    {
        var session = ClosedSession();

        DomainAssert.Throws(TableSessionErrors.ClosedIsImmutable, () => session.ChangeGuestCount(8, _clock));
        DomainAssert.Throws(TableSessionErrors.ClosedIsImmutable, () => session.ChangeGuestCount(session.GuestCount, _clock));

        Assert.Equal(2, session.GuestCount);
        DomainAssert.NoEvents(session);
    }
}

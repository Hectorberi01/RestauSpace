namespace Floor.Domain;

/// <summary>Occupation d'une table pendant un service, de l'installation des convives à la libération de la table.</summary>
public sealed class TableSession : AggregateRoot<TableSessionId>, ITenantOwned
{
    private TableSession(
        TableSessionId id,
        TenantId tenantId,
        LocationId locationId,
        TableId tableId,
        int guestCount,
        ReservationReference? reservation,
        DateTimeOffset openedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        TableId = tableId;
        GuestCount = guestCount;
        Reservation = reservation;
        OpenedAtUtc = openedAtUtc;
    }

    // Réservé à l'ORM.
    private TableSession()
    {
    }

    public TenantId TenantId { get; private set; }

    /// <summary>Établissement de la table, copié depuis la <see cref="TableReference"/> à l'ouverture.</summary>
    public LocationId LocationId { get; private set; }

    public TableId TableId { get; private set; }

    public int GuestCount { get; private set; }

    /// <summary>Réservation pour laquelle les convives ont été installés ; null pour un client sans réservation.</summary>
    public ReservationReference? Reservation { get; private set; }

    public bool IsWalkIn => Reservation is null;

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool IsOpen => ClosedAtUtc is null;

    /// <param name="tenantId">Tenant pour lequel la session est ouverte ; doit posséder <paramref name="table"/>.</param>
    /// <param name="tableHasOpenSession">Réponse de <see cref="ITableOccupancyChecker"/> pour cette table.</param>
    /// <param name="reservation">Réservation des convives, telle que connue de l'Application ; null sans réservation.</param>
    public static TableSession Open(
        TenantId tenantId,
        TableReference table,
        int guestCount,
        bool tableHasOpenSession,
        IClock clock,
        ReservationReference? reservation = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        if (reservation is { } reservationReference)
        {
            Guard.NotEmpty(reservationReference);
        }

        DomainException.ThrowIf(table.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(!table.IsInService, TableSessionErrors.TableOutOfService);
        DomainException.ThrowIf(tableHasOpenSession, TableSessionErrors.TableAlreadyOccupied);
        EnsureValidGuestCount(guestCount);

        var now = clock.UtcNow;
        var session = new TableSession(
            TableSessionId.New(), tenantId, table.LocationId, table.TableId, guestCount, reservation, now);
        session.Raise(new TableSessionOpened(
            tenantId, session.Id, table.LocationId, table.TableId, guestCount, reservation, now));
        return session;
    }

    public void ChangeGuestCount(int guestCount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, TableSessionErrors.ClosedIsImmutable);
        EnsureValidGuestCount(guestCount);
        if (guestCount == GuestCount)
        {
            return;
        }

        var previous = GuestCount;
        GuestCount = guestCount;
        Raise(new TableSessionGuestCountChanged(TenantId, Id, LocationId, TableId, previous, guestCount, clock.UtcNow));
    }

    /// <summary>
    /// Les convives changent de table : même établissement, table en service et libre. Les commandes suivent la session.
    /// </summary>
    /// <param name="targetHasOpenSession">Réponse de <see cref="ITableOccupancyChecker"/> pour la table d'arrivée.</param>
    public void MoveTo(TableReference table, bool targetHasOpenSession, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, TableSessionErrors.ClosedIsImmutable);
        DomainException.ThrowIf(table.TenantId != TenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(table.LocationId != LocationId, TableSessionErrors.OtherLocation);
        if (table.TableId == TableId)
        {
            return;
        }

        DomainException.ThrowIf(!table.IsInService, TableSessionErrors.TableOutOfService);
        DomainException.ThrowIf(targetHasOpenSession, TableSessionErrors.TableAlreadyOccupied);

        var previous = TableId;
        TableId = table.TableId;
        Raise(new TableSessionMoved(TenantId, Id, LocationId, previous, table.TableId, clock.UtcNow));
    }

    /// <summary>
    /// Fusion : les convives de cette session rejoignent <paramref name="target"/> (même établissement, ouverte), qui
    /// compte désormais leurs couverts ; cette session est fermée. Ses commandes ont été déplacées avant par l'appelant.
    /// </summary>
    public void MergeInto(TableSession target, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(target.Id == Id, TableSessionErrors.MergeWithItself);
        DomainException.ThrowIf(!IsOpen || !target.IsOpen, TableSessionErrors.ClosedIsImmutable);
        DomainException.ThrowIf(target.TenantId != TenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(target.LocationId != LocationId, TableSessionErrors.OtherLocation);

        target.ChangeGuestCount(target.GuestCount + GuestCount, clock);
        var now = clock.UtcNow;
        ClosedAtUtc = now;
        Raise(new TableSessionMerged(TenantId, Id, target.Id, LocationId, TableId, target.TableId, now));
        Raise(new TableSessionClosed(TenantId, Id, LocationId, TableId, GuestCount, Reservation, OpenedAtUtc, now));
    }

    /// <param name="hasOpenOrders">Réponse de <see cref="ITableSessionOrdersChecker"/> pour cette session.</param>
    public void Close(bool hasOpenOrders, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, TableSessionErrors.AlreadyClosed);
        DomainException.ThrowIf(hasOpenOrders, TableSessionErrors.HasOpenOrders);

        var now = clock.UtcNow;
        ClosedAtUtc = now;
        Raise(new TableSessionClosed(TenantId, Id, LocationId, TableId, GuestCount, Reservation, OpenedAtUtc, now));
    }

    private static void EnsureValidGuestCount(int guestCount) =>
        DomainException.ThrowIf(guestCount <= 0, TableSessionErrors.GuestCountNotPositive);
}

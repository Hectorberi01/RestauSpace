using Floor.Domain;

namespace Floor.Application.Queries;

public sealed record TableLayoutDto(int X, int Y, int Width, int Height, TableShape Shape, int Rotation = 0);

/// <param name="OpenSessionId">Session en cours sur la table ; null si elle est libre.</param>
/// <param name="Layout">Place sur le plan dessiné ; null tant que la table n'y est pas posée.</param>
public sealed record TableDto(Guid Id, string Label, int Capacity, TableStatus Status, bool IsInService, Guid? OpenSessionId, TableLayoutDto? Layout = null);

/// <param name="Tables">Dans l'ordre où elles ont été ajoutées.</param>
public sealed record DiningAreaDto(Guid Id, Guid LocationId, string Name, int SeatsInService, IReadOnlyList<TableDto> Tables)
{
    /// <param name="openSessions">Sessions ouvertes du tenant, par table.</param>
    public static DiningAreaDto From(DiningArea area, IReadOnlyDictionary<TableId, TableSessionId> openSessions)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(openSessions);
        return new DiningAreaDto(
            area.Id.Value,
            area.LocationId.Value,
            area.Name,
            area.SeatsInService,
            area.Tables
                .Select(table => new TableDto(
                    table.Id.Value,
                    table.Label.Value,
                    table.Capacity,
                    table.Status,
                    table.IsInService,
                    openSessions.TryGetValue(table.Id, out var session) ? session.Value : null,
                    table.Layout is { } layout ? new TableLayoutDto(layout.X, layout.Y, layout.Width, layout.Height, layout.Shape, layout.Rotation) : null))
                .ToList());
    }
}

/// <param name="ReservationId">Réservation honorée ; null pour un client sans réservation.</param>
public sealed record TableSessionDto(
    Guid Id,
    Guid LocationId,
    Guid TableId,
    int GuestCount,
    Guid? ReservationId,
    bool IsOpen,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc)
{
    public static TableSessionDto From(TableSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new TableSessionDto(
            session.Id.Value,
            session.LocationId.Value,
            session.TableId.Value,
            session.GuestCount,
            session.Reservation?.Value,
            session.IsOpen,
            session.OpenedAtUtc,
            session.ClosedAtUtc);
    }
}

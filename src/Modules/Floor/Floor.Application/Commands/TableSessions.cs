using Floor.Application.Abstractions;
using Floor.Domain;

namespace Floor.Application.Commands;

// Service : une session = une occupation réelle d'une table, de l'installation des convives à la libération.

/// <summary>
/// Installe des convives sur une table en service et libre du tenant courant. <paramref name="ReservationId"/> : la
/// réservation honorée (id opaque, non vérifié par Floor) ; null pour un client sans réservation. Renvoie l'id de la session.
/// </summary>
public sealed record OpenTableSessionCommand(Guid TableId, int GuestCount, Guid? ReservationId = null) : ICommand<Guid>;

public sealed class OpenTableSessionHandler(FloorWorkspace workspace, ITableOccupancyChecker occupancy, IClock clock)
    : ICommandHandler<OpenTableSessionCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenTableSessionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var area = await workspace.LoadDiningAreaOfTableAsync(tenant, command.TableId, cancellationToken);
        var table = area.GetTableReference(TableId.From(command.TableId));
        var reservation = command.ReservationId is { } reservationId ? ReservationReference.From(reservationId) : (ReservationReference?)null;

        // Le contrôle préalable donne une erreur claire ; l'index unique en base tranche entre deux ouvertures simultanées.
        var session = TableSession.Open(
            tenant,
            table,
            command.GuestCount,
            await occupancy.HasOpenSessionAsync(tenant, table.TableId, cancellationToken),
            clock,
            reservation);

        workspace.Sessions.Add(session);
        await workspace.SaveAsync(cancellationToken);
        return session.Id.Value;
    }
}

public sealed record ChangeGuestCountCommand(Guid TableSessionId, int GuestCount) : ICommand;

public sealed class ChangeGuestCountHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<ChangeGuestCountCommand>
{
    public Task HandleAsync(ChangeGuestCountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditSessionAsync(
            command.TableSessionId, session => session.ChangeGuestCount(command.GuestCount, clock), cancellationToken);
}

/// <summary>Libère la table. Refusé tant qu'une commande de la session est ouverte (réponse du module Orders).</summary>
public sealed record CloseTableSessionCommand(Guid TableSessionId) : ICommand;

public sealed class CloseTableSessionHandler(FloorWorkspace workspace, ITableSessionOrdersChecker orders, IClock clock)
    : ICommandHandler<CloseTableSessionCommand>
{
    public async Task HandleAsync(CloseTableSessionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var session = await workspace.LoadSessionAsync(tenant, command.TableSessionId, cancellationToken);

        // Une session déjà fermée répond « déjà fermée » sans interroger Orders.
        var hasOpenOrders = session.IsOpen && await orders.HasOpenOrdersAsync(tenant, session.Id, cancellationToken);
        session.Close(hasOpenOrders, clock);

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Les convives d'une session passent à une autre table du même établissement, libre et en service.</summary>
public sealed record MoveTableSessionCommand(Guid TableSessionId, Guid TableId) : ICommand;

public sealed class MoveTableSessionHandler(FloorWorkspace workspace, ITableOccupancyChecker occupancy, IClock clock)
    : ICommandHandler<MoveTableSessionCommand>
{
    public async Task HandleAsync(MoveTableSessionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var session = await workspace.LoadSessionAsync(tenant, command.TableSessionId, cancellationToken);
        var area = await workspace.LoadDiningAreaOfTableAsync(tenant, command.TableId, cancellationToken);
        var table = area.GetTableReference(TableId.From(command.TableId));

        session.MoveTo(table, table.TableId != session.TableId && await occupancy.HasOpenSessionAsync(tenant, table.TableId, cancellationToken), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Réunit deux tables : les commandes en cours de <paramref name="TableSessionId"/> passent sur
/// <paramref name="IntoTableSessionId"/> (module Orders, via l'hôte), puis la première session est fermée et ses couverts
/// s'ajoutent à l'autre. Rejouable : sans commande restante, seule la fermeture se fait.
/// </summary>
public sealed record MergeTableSessionsCommand(Guid TableSessionId, Guid IntoTableSessionId) : ICommand;

public sealed class MergeTableSessionsHandler(FloorWorkspace workspace, ITableSessionOrdersMover orders, IClock clock)
    : ICommandHandler<MergeTableSessionsCommand>
{
    public async Task HandleAsync(MergeTableSessionsCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var source = await workspace.LoadSessionAsync(tenant, command.TableSessionId, cancellationToken);
        var target = await workspace.LoadSessionAsync(tenant, command.IntoTableSessionId, cancellationToken);
        DomainException.ThrowIf(source.Id == target.Id, TableSessionErrors.MergeWithItself);
        DomainException.ThrowIf(!source.IsOpen || !target.IsOpen, TableSessionErrors.ClosedIsImmutable);
        DomainException.ThrowIf(source.LocationId != target.LocationId, TableSessionErrors.OtherLocation);

        await orders.MoveOpenOrdersAsync(tenant, source.Id, target.Id, cancellationToken);
        source.MergeInto(target, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RecordSessionOrderOpenedCommand(Guid OrderId, Guid TableSessionId) : ICommand;

public sealed class RecordSessionOrderOpenedHandler(FloorWorkspace workspace, ITableSessionOrderLedger ledger)
    : ICommandHandler<RecordSessionOrderOpenedCommand>
{
    public Task HandleAsync(RecordSessionOrderOpenedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DomainException.ThrowIf(command.OrderId == Guid.Empty, CommonErrors.EmptyId);
        return ledger.RecordOpenedAsync(workspace.CurrentTenant, command.OrderId, TableSessionId.From(command.TableSessionId), cancellationToken);
    }
}

public sealed record RecordSessionOrderClosedCommand(Guid OrderId) : ICommand;

public sealed class RecordSessionOrderClosedHandler(FloorWorkspace workspace, ITableSessionOrderLedger ledger)
    : ICommandHandler<RecordSessionOrderClosedCommand>
{
    public Task HandleAsync(RecordSessionOrderClosedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DomainException.ThrowIf(command.OrderId == Guid.Empty, CommonErrors.EmptyId);
        return ledger.RecordClosedAsync(workspace.CurrentTenant, command.OrderId, cancellationToken);
    }
}

using Kitchen.Application.Abstractions;
using Kitchen.Domain;

namespace Kitchen.Application.Commands;

// Tickets cuisine : un par commande acceptée. Queued -> InProgress -> Ready -> Completed ; Cancelled avant la fin.

/// <summary>
/// Crée LE ticket d'une commande acceptée du tenant courant : chaque ligne est routée par le routage de l'établissement
/// vers son poste (ou le poste par défaut). Idempotent : si la commande a déjà son ticket, renvoie son id sans rien
/// changer (événement rejoué, nouvelle tentative manuelle). Renvoie l'id du ticket.
/// </summary>
public sealed record CreateTicketForOrderCommand(Guid OrderId) : ICommand<Guid>;

public sealed class CreateTicketForOrderHandler(KitchenWorkspace workspace, IKitchenOrders orders, IClock clock)
    : ICommandHandler<CreateTicketForOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateTicketForOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var orderId = command.OrderId == Guid.Empty ? (OrderId?)null : OrderId.From(command.OrderId);
        var order = (orderId is { } id ? await orders.FindAsync(tenant, id, cancellationToken) : null)
            ?? throw new NotFoundException(KitchenApplicationErrors.OrderNotFound);

        if (await workspace.Tickets.FindByOrderAsync(tenant, order.OrderId, cancellationToken) is { } existing)
        {
            return existing.Id.Value;
        }

        DomainException.ThrowIf(!order.IsAccepted, KitchenApplicationErrors.OrderNotAccepted);
        var routing = await workspace.LoadRoutingAsync(tenant, order.LocationId, cancellationToken);
        var stationIds = order.Lines.Select(line => routing.StationFor(line.ProductId)).Distinct().ToList();
        var stations = (await workspace.Stations.FindManyAsync(tenant, stationIds, cancellationToken))
            .ToDictionary(station => station.Id, station => station.ToReference());
        // Un poste routé mais introuvable ne devrait pas exister : le signaler plutôt que de router ailleurs en silence.
        DomainException.ThrowIf(stationIds.Any(stationId => !stations.ContainsKey(stationId)), KitchenTicketErrors.StationInactive);

        var snapshot = KitchenOrderSnapshot.Create(
            order.OrderId,
            tenant,
            order.LocationId,
            order.Lines.Select(line => KitchenOrderLine.Create(
                line.OrderItemId,
                line.ProductName,
                line.Quantity,
                stations[routing.StationFor(line.ProductId)],
                line.VariantName,
                line.ModifierNames,
                line.Note,
                routing.CourseFor(line.ProductId))));
        var ticket = KitchenTicket.CreateFromOrder(snapshot, KitchenPriority.Normal, clock, holdLaterCourses: order.IsDineIn);

        workspace.Tickets.Add(ticket);
        await workspace.SaveAsync(cancellationToken);
        return ticket.Id.Value;
    }
}

/// <summary>
/// Annule le ticket d'une commande annulée, avec le motif de l'annulation amont. Sans effet si la commande n'a pas de
/// ticket ou si le ticket est déjà clos.
/// </summary>
public sealed record CancelTicketForOrderCommand(Guid OrderId, string? Reason) : ICommand;

public sealed class CancelTicketForOrderHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<CancelTicketForOrderCommand>
{
    public async Task HandleAsync(CancelTicketForOrderCommand command, CancellationToken cancellationToken = default)
    {
        // Pas de contrôle « tenant accepte les écritures » : la commande a déjà été annulée, la cuisine suit.
        var tenant = workspace.CurrentTenant;
        if (command.OrderId == Guid.Empty
            || await workspace.Tickets.FindByOrderAsync(tenant, OrderId.From(command.OrderId), cancellationToken) is not { IsClosed: false } ticket)
        {
            return;
        }

        // Le motif amont peut être plus long que celui d'un ticket : tronqué plutôt que refusé.
        var reason = command.Reason is { Length: > KitchenTicket.MaxCancellationReasonLength } longReason
            ? longReason[..KitchenTicket.MaxCancellationReasonLength]
            : command.Reason;
        ticket.Cancel(clock, reason);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Queued -> InProgress, sans démarrer de ligne.</summary>
public sealed record StartTicketCommand(Guid TicketId) : ICommand;

public sealed class StartTicketHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<StartTicketCommand>
{
    public Task HandleAsync(StartTicketCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.Start(clock), cancellationToken);
}

/// <summary>Ligne Queued -> Preparing ; la première ligne démarrée démarre le ticket.</summary>
public sealed record StartTicketItemCommand(Guid TicketId, Guid ItemId) : ICommand;

public sealed class StartTicketItemHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<StartTicketItemCommand>
{
    public Task HandleAsync(StartTicketItemCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(
            command.TicketId, ticket => ticket.MarkItemStarted(KitchenTicketItemId.From(command.ItemId), clock), cancellationToken);
}

/// <summary>Ligne Preparing -> Ready. Le ticket, lui, ne passe prêt que par <see cref="MarkTicketReadyCommand"/>.</summary>
public sealed record MarkTicketItemReadyCommand(Guid TicketId, Guid ItemId) : ICommand;

public sealed class MarkTicketItemReadyHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<MarkTicketItemReadyCommand>
{
    public Task HandleAsync(MarkTicketItemReadyCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(
            command.TicketId, ticket => ticket.MarkItemReady(KitchenTicketItemId.From(command.ItemId), clock), cancellationToken);
}

/// <summary>InProgress -> Ready (le passe « bumpe » le ticket), une fois la policy de préparation satisfaite.</summary>
public sealed record MarkTicketReadyCommand(Guid TicketId) : ICommand;

public sealed class MarkTicketReadyHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<MarkTicketReadyCommand>
{
    public Task HandleAsync(MarkTicketReadyCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.MarkReady(clock), cancellationToken);
}

/// <summary>Ready -> Completed (remis au service), toutes les lignes prêtes.</summary>
public sealed record CompleteTicketCommand(Guid TicketId) : ICommand;

public sealed class CompleteTicketHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<CompleteTicketCommand>
{
    public Task HandleAsync(CompleteTicketCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.Complete(clock), cancellationToken);
}

/// <summary>Annulation décidée en cuisine ; motif facultatif. Ne touche pas à la commande.</summary>
public sealed record CancelTicketCommand(Guid TicketId, string? Reason = null) : ICommand;

public sealed class CancelTicketHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<CancelTicketCommand>
{
    public Task HandleAsync(CancelTicketCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.Cancel(clock, command.Reason), cancellationToken);
}

/// <summary>La salle envoie un service (entrée, plat, dessert) : ses plats en attente partent en préparation.</summary>
public sealed record FireCourseCommand(Guid TicketId, KitchenCourse Course) : ICommand;

public sealed class FireCourseHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<FireCourseCommand>
{
    public Task HandleAsync(FireCourseCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.FireCourse(command.Course, clock), cancellationToken);
}

public sealed record ChangeTicketPriorityCommand(Guid TicketId, KitchenPriority Priority) : ICommand;

public sealed class ChangeTicketPriorityHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<ChangeTicketPriorityCommand>
{
    public Task HandleAsync(ChangeTicketPriorityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditTicketAsync(command.TicketId, ticket => ticket.ChangePriority(command.Priority, clock), cancellationToken);
}

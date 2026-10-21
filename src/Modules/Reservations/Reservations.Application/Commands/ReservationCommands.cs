using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Application.Commands;

// Réservations prises et gérées par le personnel du restaurant : l'acteur est toujours Staff (jamais une valeur de la
// requête). Les règles (fenêtre de réservation, durées, taille de groupe, transitions) sont celles de l'agrégat ; ici,
// l'ordre des étapes et le verrou de capacité.

/// <summary>
/// Prend une réservation en attente (Pending). <paramref name="StartAt"/> est un instant avec son décalage horaire ;
/// <paramref name="DurationMinutes"/> null = durée par défaut de la politique de l'établissement. Au moins un téléphone
/// (E.164) ou un email pour joindre le client, même quand <paramref name="CustomerId"/> rattache une fiche client
/// (Customers) : la réservation garde son propre instantané de contact. Une fiche anonymisée est refusée. Renvoie l'id de
/// la réservation. La règle d'acompte de l'établissement s'applique (la réservation attend alors son paiement), sauf
/// <paramref name="WithoutDeposit"/>.
/// </summary>
public sealed record CreateReservationCommand(
    Guid LocationId,
    DateTimeOffset StartAt,
    int PartySize,
    string? GuestName,
    string? GuestPhone = null,
    string? GuestEmail = null,
    string? Notes = null,
    int? DurationMinutes = null,
    Guid? CustomerId = null,
    bool WithoutDeposit = false) : ICommand<Guid>;

public sealed class CreateReservationHandler(ReservationsWorkspace workspace, IReservationCustomers customers, IClock clock)
    : ICommandHandler<CreateReservationCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        var slot = ReservationsWorkspace.SlotFor(policy, command.StartAt, command.DurationMinutes);
        var contact = GuestContact.Create(
            command.GuestName,
            string.IsNullOrWhiteSpace(command.GuestPhone) ? null : PhoneNumber.Create(command.GuestPhone),
            string.IsNullOrWhiteSpace(command.GuestEmail) ? null : Email.Create(command.GuestEmail));
        CustomerReference? customer = null;
        if (command.CustomerId is { } customerId)
        {
            customer = (customerId == Guid.Empty
                    ? null
                    : await customers.FindAsync(tenant, CustomerId.From(customerId), cancellationToken))
                ?? throw new NotFoundException(ReservationsApplicationErrors.CustomerNotFound);
            // Règle du domaine, vérifiée avant de prendre le verrou de capacité.
            DomainException.ThrowIf(customer.IsAnonymized, ReservationErrors.CustomerAnonymized);
        }

        await workspace.EnsureOpenDuringAsync(tenant, location.LocationId, slot, cancellationToken);

        var availability = await workspace.EvaluateAsync(
            tenant, location.LocationId, slot, command.PartySize, policy, excludedReservationId: null, forWrite: true, cancellationToken);
        var reservation = Reservation.Create(
            tenant, location, customer, contact, command.PartySize, slot, command.Notes, policy, availability, clock);
        if (!command.WithoutDeposit)
        {
            await Deposits.RequireAsync(workspace, reservation, cancellationToken);
        }

        workspace.Reservations.Add(reservation);
        await workspace.SaveAsync(cancellationToken);
        return reservation.Id.Value;
    }
}

public sealed record ConfirmReservationCommand(Guid ReservationId) : ICommand;

/// <summary>
/// Rattache la réservation à une fiche client du tenant courant, ou l'en détache (<paramref name="CustomerId"/> null).
/// Une fiche inconnue ou d'un autre tenant : 404 ; une fiche anonymisée : refusée.
/// </summary>
public sealed record ChangeReservationCustomerCommand(Guid ReservationId, Guid? CustomerId) : ICommand;

public sealed class ChangeReservationCustomerHandler(ReservationsWorkspace workspace, IReservationCustomers customers, IClock clock)
    : ICommandHandler<ChangeReservationCustomerCommand>
{
    public async Task HandleAsync(ChangeReservationCustomerCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        CustomerReference? customer = null;
        if (command.CustomerId is { } customerId)
        {
            customer = (customerId == Guid.Empty
                    ? null
                    : await customers.FindAsync(tenant, CustomerId.From(customerId), cancellationToken))
                ?? throw new NotFoundException(ReservationsApplicationErrors.CustomerNotFound);
        }

        reservation.ChangeCustomer(customer, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed class ConfirmReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<ConfirmReservationCommand>
{
    public async Task HandleAsync(ConfirmReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        reservation.Confirm(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Déplace la réservation ; <paramref name="DurationMinutes"/> null = durée par défaut de la politique.</summary>
public sealed record RescheduleReservationCommand(Guid ReservationId, DateTimeOffset StartAt, int? DurationMinutes = null) : ICommand;

public sealed class RescheduleReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<RescheduleReservationCommand>
{
    public async Task HandleAsync(RescheduleReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        var slot = ReservationsWorkspace.SlotFor(policy, command.StartAt, command.DurationMinutes);
        if (slot == reservation.Slot)
        {
            // Rien ne bouge : la décision ne serait ni examinée ni consommée (règle du domaine), inutile de verrouiller.
            reservation.Reschedule(slot, ReservationActor.Staff, policy, ReservationsWorkspace.UnusedDecision(reservation), clock);
            return;
        }

        await workspace.EnsureOpenDuringAsync(tenant, reservation.LocationId, slot, cancellationToken);
        var availability = await workspace.EvaluateAsync(
            tenant, reservation.LocationId, slot, reservation.PartySize, policy, reservation.Id, forWrite: true, cancellationToken);
        reservation.Reschedule(slot, ReservationActor.Staff, policy, availability, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ChangePartySizeCommand(Guid ReservationId, int PartySize) : ICommand;

public sealed class ChangePartySizeHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<ChangePartySizeCommand>
{
    public async Task HandleAsync(ChangePartySizeCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        if (command.PartySize == reservation.PartySize)
        {
            // Rien ne change : le domaine applique ses gardes d'état sans examiner la décision ; inutile de verrouiller.
            reservation.ChangePartySize(command.PartySize, ReservationActor.Staff, policy, ReservationsWorkspace.UnusedDecision(reservation), clock);
            return;
        }

        var availability = await workspace.EvaluateAsync(
            tenant, reservation.LocationId, reservation.Slot, command.PartySize, policy, reservation.Id, forWrite: true, cancellationToken);
        reservation.ChangePartySize(command.PartySize, ReservationActor.Staff, policy, availability, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Installe le groupe (réservation confirmée). <paramref name="TableSessionId"/> : la session ouverte pour lui dans
/// Floor (ouverte, du même établissement) ; null quand l'établissement ne suit pas ses tables.
/// </summary>
public sealed record SeatReservationCommand(Guid ReservationId, Guid? TableSessionId = null) : ICommand;

public sealed class SeatReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<SeatReservationCommand>
{
    public async Task HandleAsync(SeatReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);

        TableSessionId? sessionId = null;
        if (command.TableSessionId is { } requested)
        {
            var session = (requested == Guid.Empty
                    ? null
                    : await workspace.Floor.FindSessionAsync(tenant, TableSessionId.From(requested), cancellationToken))
                ?? throw new NotFoundException(ReservationsApplicationErrors.TableSessionNotFound);
            DomainException.ThrowIf(
                !session.IsOpen || session.LocationId != reservation.LocationId, ReservationsApplicationErrors.TableSessionNotUsable);
            sessionId = session.Id;
        }

        reservation.Seat(sessionId, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CompleteReservationCommand(Guid ReservationId) : ICommand;

public sealed class CompleteReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<CompleteReservationCommand>
{
    public async Task HandleAsync(CompleteReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        reservation.Complete(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Annulation par le personnel : possible à tout moment avant l'installation (pas de délai limite). Un acompte payé est
/// remboursé, sauf <paramref name="KeepDeposit"/> (annulation tardive : le restaurant le garde).
/// </summary>
public sealed record CancelReservationCommand(Guid ReservationId, string? Reason = null, bool KeepDeposit = false) : ICommand;

public sealed class CancelReservationHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<CancelReservationCommand>
{
    public async Task HandleAsync(CancelReservationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        reservation.Cancel(ReservationActor.Staff, command.Reason, policy, clock, command.KeepDeposit);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record MarkReservationNoShowCommand(Guid ReservationId) : ICommand;

public sealed class MarkReservationNoShowHandler(ReservationsWorkspace workspace, IClock clock) : ICommandHandler<MarkReservationNoShowCommand>
{
    public async Task HandleAsync(MarkReservationNoShowCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        reservation.MarkNoShow(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Le client a été anonymisé (Customers, événement <c>CustomerAnonymized</c>) : efface le contact et les notes de TOUTES
/// ses réservations, terminées comprises. Idempotent (rejouable sans effet) et possible même pour un restaurant suspendu :
/// une obligation légale ne dépend pas de l'abonnement. Renvoie le nombre de réservations effacées.
/// </summary>
public sealed record AnonymizeCustomerReservationsCommand(Guid CustomerId) : ICommand<int>;

public sealed class AnonymizeCustomerReservationsHandler(ReservationsWorkspace workspace, IClock clock)
    : ICommandHandler<AnonymizeCustomerReservationsCommand, int>
{
    public async Task<int> HandleAsync(AnonymizeCustomerReservationsCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CustomerId == Guid.Empty)
        {
            return 0;
        }

        var reservations = await workspace.Reservations.ListForCustomerAsync(
            workspace.CurrentTenant, CustomerId.From(command.CustomerId), cancellationToken);
        var erased = reservations.Count(reservation => !reservation.IsGuestAnonymized);
        foreach (var reservation in reservations)
        {
            reservation.AnonymizeGuest(clock);
        }

        foreach (var entry in await workspace.Waitlist.ListForCustomerAsync(workspace.CurrentTenant, CustomerId.From(command.CustomerId), cancellationToken))
        {
            entry.AnonymizeGuest();
        }

        await workspace.SaveAsync(cancellationToken);
        return erased;
    }
}

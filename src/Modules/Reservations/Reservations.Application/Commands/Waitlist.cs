using Reservations.Application.Abstractions;
using Reservations.Domain;

namespace Reservations.Application.Commands;

// Liste d'attente des créneaux complets. Quand des places se libèrent (annulation, no-show, groupe réduit, réservation
// déplacée, offre passée), l'hôte demande OfferFreedCapacity : la plus ancienne inscription qui tient dans la capacité
// reçoit une offre, qui retient les places jusqu'à son expiration. Le client l'accepte par son lien (une réservation est
// créée, avec l'acompte de la règle) ; sinon elle passe au suivant.

public sealed record WaitlistSettingsDto(bool AcceptsOnline, int OfferWindowMinutes)
{
    public static WaitlistSettingsDto From(WaitlistSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new WaitlistSettingsDto(settings.AcceptsOnline, (int)settings.OfferWindow.TotalMinutes);
    }
}

public sealed record GetWaitlistSettingsQuery(Guid LocationId) : IQuery<WaitlistSettingsDto>;

public sealed class GetWaitlistSettingsHandler(ReservationsWorkspace workspace) : IQueryHandler<GetWaitlistSettingsQuery, WaitlistSettingsDto>
{
    public async Task<WaitlistSettingsDto> HandleAsync(GetWaitlistSettingsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        return WaitlistSettingsDto.From(await workspace.Policies.GetWaitlistSettingsAsync(tenant, location.LocationId, cancellationToken));
    }
}

public sealed record SetWaitlistSettingsCommand(Guid LocationId, bool AcceptsOnline, int OfferWindowMinutes = 15) : ICommand;

public sealed class SetWaitlistSettingsHandler(ReservationsWorkspace workspace) : ICommandHandler<SetWaitlistSettingsCommand>
{
    public async Task HandleAsync(SetWaitlistSettingsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var settings = WaitlistSettings.Create(command.AcceptsOnline, TimeSpan.FromMinutes(Math.Clamp(command.OfferWindowMinutes, 0, 1000)));
        await workspace.Policies.SetWaitlistSettingsAsync(tenant, location.LocationId, settings, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <param name="GuestPhone">Obligatoire : c'est par lui que le client est prévenu.</param>
/// <param name="FlexibilityMinutes">Minutes acceptées avant et après l'heure souhaitée (0 à 180).</param>
/// <param name="Online">Inscription par le client sur le site (l'hôte) ; sinon par l'équipe.</param>
public sealed record JoinWaitlistCommand(
    Guid LocationId,
    DateTimeOffset DesiredStart,
    int PartySize,
    int FlexibilityMinutes,
    string? GuestName,
    string? GuestPhone,
    string? GuestEmail = null,
    string? Notes = null,
    Guid? CustomerId = null,
    bool Online = false) : ICommand<Guid>
{
    public override string ToString() => $"JoinWaitlistCommand ({LocationId}, {DesiredStart:O}, {PartySize}, ***)";
}

public sealed class JoinWaitlistHandler(ReservationsWorkspace workspace, IReservationCustomers customers) : ICommandHandler<JoinWaitlistCommand, Guid>
{
    public async Task<Guid> HandleAsync(JoinWaitlistCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        if (command.Online)
        {
            var settings = await workspace.Policies.GetWaitlistSettingsAsync(tenant, location.LocationId, cancellationToken);
            DomainException.ThrowIf(!settings.AcceptsOnline || !policy.AcceptsOnlineBookings, ReservationsApplicationErrors.WaitlistDisabled);
            // Encore de la place à cette heure : le client réserve directement.
            var slot = policy.SlotFor(command.DesiredStart);
            if (policy.CanBookAt(slot.StartUtc, workspace.Clock) && command.PartySize >= 1 && command.PartySize <= policy.MaxPartySize)
            {
                var decision = await workspace.EvaluateAsync(
                    tenant, location.LocationId, slot, command.PartySize, policy, excludedReservationId: null, forWrite: false, cancellationToken);
                DomainException.ThrowIf(decision.IsAvailable, ReservationsApplicationErrors.SlotAvailable);
            }
        }

        DomainException.ThrowIf(string.IsNullOrWhiteSpace(command.GuestPhone), WaitlistErrors.PhoneRequired);
        var contact = GuestContact.Create(
            command.GuestName,
            PhoneNumber.Create(command.GuestPhone),
            string.IsNullOrWhiteSpace(command.GuestEmail) ? null : Email.Create(command.GuestEmail));
        var customer = command.CustomerId is { } customerId && customerId != Guid.Empty
            ? await customers.FindAsync(tenant, CustomerId.From(customerId), cancellationToken) is { IsAnonymized: false } found ? found : null
            : null;
        var entry = WaitlistEntry.Create(
            tenant, location, customer, contact, command.PartySize, command.DesiredStart, command.FlexibilityMinutes, command.Notes,
            command.Online ? WaitlistSource.Online : WaitlistSource.Staff, policy, workspace.Clock);
        workspace.Waitlist.Add(entry);
        await workspace.SaveAsync(cancellationToken);
        return entry.Id.Value;
    }
}

/// <summary>
/// Des places se sont peut-être libérées dans l'établissement : propose-les aux inscriptions en attente, les plus
/// anciennes d'abord, à l'heure la plus proche de leur souhait (par pas de 15 minutes dans leur souplesse). Une offre
/// retient les places pour le temps de réponse. Renvoie le nombre d'offres faites.
/// </summary>
public sealed record OfferFreedCapacityCommand(Guid LocationId) : ICommand<int>;

public sealed class OfferFreedCapacityHandler(ReservationsWorkspace workspace) : ICommandHandler<OfferFreedCapacityCommand, int>
{
    public const int StepMinutes = 15;
    public const int MaxEntriesPerPass = 20;

    public async Task<int> HandleAsync(OfferFreedCapacityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        if (!location.IsOperational)
        {
            return 0;
        }

        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        var settings = await workspace.Policies.GetWaitlistSettingsAsync(tenant, location.LocationId, cancellationToken);
        var clock = workspace.Clock;
        var now = clock.UtcNow;
        var waiting = (await workspace.Waitlist.ListOpenAsync(tenant, location.LocationId, cancellationToken))
            .Where(entry => entry.Status == WaitlistStatus.Waiting && entry.LatestStartUtc > now)
            .Take(MaxEntriesPerPass)
            .ToList();
        if (waiting.Count == 0)
        {
            return 0;
        }

        var offered = 0;
        var locked = false;
        foreach (var entry in waiting)
        {
            foreach (var start in Candidates(entry, policy, clock))
            {
                var slot = policy.SlotFor(start);
                if (!await OpenDuringAsync(tenant, location.LocationId, slot, cancellationToken))
                {
                    continue;
                }

                var decision = await workspace.EvaluateAsync(
                    tenant, location.LocationId, slot, entry.PartySize, policy, excludedReservationId: null, forWrite: !locked, cancellationToken);
                locked = true;
                if (!decision.IsAvailable)
                {
                    continue;
                }

                var expires = now + settings.OfferWindow;
                entry.Offer(start, expires < start ? expires : start, clock);
                offered++;
                break;
            }
        }

        await workspace.SaveAsync(cancellationToken);
        return offered;
    }

    /// <summary>Débuts acceptés et réservables, du plus proche au plus éloigné de l'heure souhaitée.</summary>
    internal static IEnumerable<DateTimeOffset> Candidates(WaitlistEntry entry, ReservationPolicy policy, IClock clock)
    {
        var starts = new List<DateTimeOffset> { entry.DesiredStartUtc };
        for (var delta = StepMinutes; delta <= entry.FlexibilityMinutes; delta += StepMinutes)
        {
            starts.Add(entry.DesiredStartUtc.AddMinutes(-delta));
            starts.Add(entry.DesiredStartUtc.AddMinutes(delta));
        }

        return starts.Where(start => policy.CanBookAt(start, clock));
    }

    private async Task<bool> OpenDuringAsync(TenantId tenant, LocationId location, Period slot, CancellationToken cancellationToken)
    {
        try
        {
            await workspace.EnsureOpenDuringAsync(tenant, location, slot, cancellationToken);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}

/// <summary>
/// Le client accepte la place proposée (lien de l'offre) : sa réservation est créée à l'heure proposée, confirmée, ou en
/// attente de l'acompte que la règle demande. Renvoie l'id de la réservation.
/// </summary>
public sealed record AcceptWaitlistOfferCommand(Guid EntryId) : ICommand<Guid>;

public sealed class AcceptWaitlistOfferHandler(ReservationsWorkspace workspace, IReservationCustomers customers) : ICommandHandler<AcceptWaitlistOfferCommand, Guid>
{
    public async Task<Guid> HandleAsync(AcceptWaitlistOfferCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var entry = await workspace.LoadWaitlistEntryAsync(tenant, command.EntryId, cancellationToken);
        var clock = workspace.Clock;
        DomainException.ThrowIf(entry.Status != WaitlistStatus.Offered, WaitlistErrors.NotOffered);
        DomainException.ThrowIf(!entry.HasLiveOffer(clock.UtcNow), WaitlistErrors.OfferExpired);
        var reservation = await BookAsync(workspace, customers, entry, entry.OfferedStartUtc!.Value, ReservationActorFor.Guest, cancellationToken);
        return reservation.Id.Value;
    }

    internal static async Task<Reservation> BookAsync(
        ReservationsWorkspace workspace, IReservationCustomers customers, WaitlistEntry entry, DateTimeOffset start, ReservationActorFor actor,
        CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var clock = workspace.Clock;
        var location = await workspace.RequireLocationAsync(tenant, entry.LocationId.Value, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        var slot = policy.SlotFor(start);
        await workspace.EnsureOpenDuringAsync(tenant, location.LocationId, slot, cancellationToken);
        var availability = await workspace.EvaluateAsync(
            tenant, location.LocationId, slot, entry.PartySize, policy, excludedReservationId: null, forWrite: true, cancellationToken, excludedOffer: entry.Id);
        DomainException.ThrowIf(!availability.IsAvailable, ReservationsApplicationErrors.OfferNoLongerAvailable);
        var customer = entry.CustomerId is { } customerId
            ? await customers.FindAsync(tenant, customerId, cancellationToken) is { IsAnonymized: false } found ? found : null
            : null;
        var reservation = Reservation.Create(
            tenant, location, customer, entry.GuestContact, entry.PartySize, slot, entry.Notes, policy, availability, clock);
        var withDeposit = await Deposits.RequireAsync(workspace, reservation, cancellationToken);
        if (!withDeposit && actor == ReservationActorFor.Guest)
        {
            reservation.Confirm(clock);
        }

        entry.MarkBooked(reservation.Id, clock);
        workspace.Reservations.Add(reservation);
        await workspace.SaveAsync(cancellationToken);
        return reservation;
    }
}

internal enum ReservationActorFor
{
    Guest,
    Staff,
}

/// <summary>
/// L'équipe réserve directement pour une inscription, à l'heure choisie (capacité vérifiée). La réservation est en
/// attente (à confirmer comme une réservation prise par téléphone), avec l'acompte de la règle. Renvoie son id.
/// </summary>
public sealed record BookWaitlistEntryCommand(Guid EntryId, DateTimeOffset StartAt) : ICommand<Guid>;

public sealed class BookWaitlistEntryHandler(ReservationsWorkspace workspace, IReservationCustomers customers) : ICommandHandler<BookWaitlistEntryCommand, Guid>
{
    public async Task<Guid> HandleAsync(BookWaitlistEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var entry = await workspace.LoadWaitlistEntryAsync(workspace.CurrentTenant, command.EntryId, cancellationToken);
        DomainException.ThrowIf(!entry.IsOpen, WaitlistErrors.AlreadyClosed);
        var reservation = await AcceptWaitlistOfferHandler.BookAsync(workspace, customers, entry, command.StartAt.ToUniversalTime(), ReservationActorFor.Staff, cancellationToken);
        return reservation.Id.Value;
    }
}

/// <summary>Le client ne veut pas de la place proposée, ou retire son inscription ; l'équipe peut aussi la retirer.</summary>
public sealed record CancelWaitlistEntryCommand(Guid EntryId) : ICommand;

public sealed class CancelWaitlistEntryHandler(ReservationsWorkspace workspace) : ICommandHandler<CancelWaitlistEntryCommand>
{
    public async Task HandleAsync(CancelWaitlistEntryCommand command, CancellationToken cancellationToken = default)
    {
        var entry = await workspace.LoadWaitlistEntryAsync(workspace.CurrentTenant, command.EntryId, cancellationToken);
        if (!entry.IsOpen)
        {
            return;
        }

        entry.Cancel(workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Pour le job : libère une offre expirée, ou ferme une inscription dont l'heure est passée. Sans effet sinon.</summary>
public sealed record ResolveWaitlistEntryCommand(Guid EntryId) : ICommand;

public sealed class ResolveWaitlistEntryHandler(ReservationsWorkspace workspace) : ICommandHandler<ResolveWaitlistEntryCommand>
{
    public async Task HandleAsync(ResolveWaitlistEntryCommand command, CancellationToken cancellationToken = default)
    {
        var entry = await workspace.LoadWaitlistEntryAsync(workspace.CurrentTenant, command.EntryId, cancellationToken);
        var clock = workspace.Clock;
        var now = clock.UtcNow;
        if (entry.Status == WaitlistStatus.Offered && entry.OfferExpiresAtUtc <= now)
        {
            entry.ReleaseOffer(clock);
        }
        else if (entry.IsOpen && entry.Status == WaitlistStatus.Waiting && entry.LatestStartUtc <= now)
        {
            entry.Expire(clock);
        }
        else
        {
            return;
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <param name="OfferedStartUtc">Heure proposée tant qu'une offre est en cours.</param>
public sealed record WaitlistEntryDto(
    Guid Id,
    Guid LocationId,
    WaitlistStatus Status,
    WaitlistSource Source,
    DateTimeOffset DesiredStartUtc,
    int FlexibilityMinutes,
    int PartySize,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? Notes,
    DateTimeOffset? OfferedStartUtc,
    DateTimeOffset? OfferExpiresAtUtc,
    int MissedOffers,
    Guid? ReservationId,
    DateTimeOffset CreatedAtUtc)
{
    public static WaitlistEntryDto From(WaitlistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new WaitlistEntryDto(
            entry.Id.Value, entry.LocationId.Value, entry.Status, entry.Source, entry.DesiredStartUtc, entry.FlexibilityMinutes, entry.PartySize,
            entry.GuestContact.Name, entry.GuestContact.Phone?.Value, entry.GuestContact.Email?.Value, entry.Notes, entry.OfferedStartUtc,
            entry.OfferExpiresAtUtc, entry.MissedOffers, entry.ReservationId?.Value, entry.CreatedAtUtc);
    }
}

/// <summary>Inscriptions dont l'heure souhaitée tombe dans [from, to[ (31 jours max), par heure souhaitée.</summary>
public sealed record ListWaitlistQuery(DateTimeOffset From, DateTimeOffset To, Guid? LocationId = null) : IQuery<IReadOnlyList<WaitlistEntryDto>>;

public sealed class ListWaitlistHandler(ReservationsWorkspace workspace) : IQueryHandler<ListWaitlistQuery, IReadOnlyList<WaitlistEntryDto>>
{
    public async Task<IReadOnlyList<WaitlistEntryDto>> HandleAsync(ListWaitlistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.From >= query.To || query.To - query.From > TimeSpan.FromDays(31), ReservationsApplicationErrors.WindowInvalid);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var entries = await workspace.Waitlist.ListAsync(
            workspace.CurrentTenant, query.LocationId is { } location ? LocationId.From(location) : null,
            Period.Create(query.From.ToUniversalTime(), query.To.ToUniversalTime()), cancellationToken);
        return entries.OrderBy(entry => entry.DesiredStartUtc).ThenBy(entry => entry.CreatedAtUtc).Select(WaitlistEntryDto.From).ToList();
    }
}

/// <summary>Ce que le client voit de son inscription (lien signé) : jamais les notes de l'équipe.</summary>
public sealed record GuestWaitlistEntryDto(
    Guid Id, Guid LocationId, WaitlistStatus Status, DateTimeOffset DesiredStartUtc, int FlexibilityMinutes, int PartySize, string GuestName,
    DateTimeOffset? OfferedStartUtc, DateTimeOffset? OfferExpiresAtUtc, Guid? ReservationId);

public sealed record GetGuestWaitlistEntryQuery(Guid EntryId) : IQuery<GuestWaitlistEntryDto>;

public sealed class GetGuestWaitlistEntryHandler(ReservationsWorkspace workspace) : IQueryHandler<GetGuestWaitlistEntryQuery, GuestWaitlistEntryDto>
{
    public async Task<GuestWaitlistEntryDto> HandleAsync(GetGuestWaitlistEntryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entry = await workspace.LoadWaitlistEntryAsync(workspace.CurrentTenant, query.EntryId, cancellationToken);
        var live = entry.HasLiveOffer(workspace.Clock.UtcNow);
        return new GuestWaitlistEntryDto(
            entry.Id.Value, entry.LocationId.Value, entry.Status, entry.DesiredStartUtc, entry.FlexibilityMinutes, entry.PartySize, entry.GuestContact.Name,
            live ? entry.OfferedStartUtc : null, live ? entry.OfferExpiresAtUtc : null, entry.ReservationId?.Value);
    }
}

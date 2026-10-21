using Reservations.Domain;

namespace Reservations.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une ressource d'un
// autre tenant n'est jamais trouvée.

public sealed record GetReservationByIdQuery(Guid ReservationId) : IQuery<ReservationDto>;

public sealed class GetReservationByIdHandler(ReservationsWorkspace workspace) : IQueryHandler<GetReservationByIdQuery, ReservationDto>
{
    public async Task<ReservationDto> HandleAsync(GetReservationByIdQuery query, CancellationToken cancellationToken = default) =>
        ReservationDto.From(await workspace.LoadReservationAsync(workspace.CurrentTenant, query.ReservationId, cancellationToken));
}

/// <summary>
/// Agenda : réservations qui commencent dans [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>[ (31 jours au plus), par
/// heure de début. <paramref name="LocationId"/> null = tous les établissements ; <paramref name="Status"/> null = tous.
/// </summary>
public sealed record ListReservationsQuery(
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? LocationId = null, ReservationStatus? Status = null)
    : IQuery<IReadOnlyList<ReservationDto>>
{
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);
}

public sealed class ListReservationsHandler(ReservationsWorkspace workspace)
    : IQueryHandler<ListReservationsQuery, IReadOnlyList<ReservationDto>>
{
    public async Task<IReadOnlyList<ReservationDto>> HandleAsync(ListReservationsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(
            query.FromUtc >= query.ToUtc || query.ToUtc - query.FromUtc > ListReservationsQuery.MaxWindow,
            ReservationsApplicationErrors.WindowInvalid);
        // Un id vide ne désigne aucun établissement : liste vide plutôt qu'erreur, comme un id inconnu.
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var location = query.LocationId is { } id ? LocationId.From(id) : (LocationId?)null;
        var reservations = await workspace.Reservations.ListAsync(
            tenant, location, Period.Create(query.FromUtc, query.ToUtc), query.Status, cancellationToken);
        return reservations.Select(ReservationDto.From).ToList();
    }
}

/// <summary>Historique des réservations d'un client, les plus récentes d'abord. Un client inconnu : page vide.</summary>
public sealed record ListCustomerReservationsQuery(Guid CustomerId, int Page = 1, int PageSize = ListCustomerReservationsQuery.DefaultPageSize)
    : IQuery<PagedResult<ReservationDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPage = 100_000;
}

public sealed class ListCustomerReservationsHandler(ReservationsWorkspace workspace)
    : IQueryHandler<ListCustomerReservationsQuery, PagedResult<ReservationDto>>
{
    public async Task<PagedResult<ReservationDto>> HandleAsync(ListCustomerReservationsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(
            query.Page is < 1 or > ListCustomerReservationsQuery.MaxPage || query.PageSize is < 1 or > ListCustomerReservationsQuery.MaxPageSize,
            ReservationsApplicationErrors.PagingInvalid);
        if (query.CustomerId == Guid.Empty)
        {
            return PagedResult<ReservationDto>.Counted([], query.Page, query.PageSize, 0);
        }

        var page = await workspace.Reservations.ListPageForCustomerAsync(
            tenant, CustomerId.From(query.CustomerId), (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        return PagedResult<ReservationDto>.Counted(page.Items.Select(ReservationDto.From).ToList(), query.Page, query.PageSize, page.TotalCount);
    }
}

/// <summary>
/// Disponibilité d'un créneau pour un groupe, sans rien réserver (lecture sans verrou : la réponse peut changer avant la
/// réservation, qui revérifie sous verrou). <paramref name="DurationMinutes"/> null = durée par défaut de la politique.
/// </summary>
public sealed record GetAvailabilityQuery(Guid LocationId, DateTimeOffset StartAt, int PartySize, int? DurationMinutes = null)
    : IQuery<AvailabilityDto>;

public sealed class GetAvailabilityHandler(ReservationsWorkspace workspace) : IQueryHandler<GetAvailabilityQuery, AvailabilityDto>
{
    public async Task<AvailabilityDto> HandleAsync(GetAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        var policy = await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken);
        var slot = ReservationsWorkspace.SlotFor(policy, query.StartAt, query.DurationMinutes);

        var open = true;
        try
        {
            await workspace.EnsureOpenDuringAsync(tenant, location.LocationId, slot, cancellationToken);
        }
        catch (DomainException exception) when (exception.Error == ReservationsApplicationErrors.LocationClosedDuringSlot)
        {
            open = false;
        }

        var decision = await workspace.EvaluateAsync(
            tenant, location.LocationId, slot, query.PartySize, policy, excludedReservationId: null, forWrite: false, cancellationToken);
        return new AvailabilityDto(
            slot.StartUtc,
            (int)slot.Duration.TotalMinutes,
            query.PartySize,
            open,
            open && decision.IsAvailable,
            decision.Capacity,
            decision.PeakBookedSeats,
            decision.RemainingSeats);
    }
}

/// <summary>Politique de réservation d'un établissement (celle par défaut tant qu'aucune n'a été réglée).</summary>
public sealed record GetReservationPolicyQuery(Guid LocationId) : IQuery<ReservationPolicyDto>;

public sealed class GetReservationPolicyHandler(ReservationsWorkspace workspace) : IQueryHandler<GetReservationPolicyQuery, ReservationPolicyDto>
{
    public async Task<ReservationPolicyDto> HandleAsync(GetReservationPolicyQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        return ReservationPolicyDto.From(await workspace.Policies.GetAsync(tenant, location.LocationId, cancellationToken));
    }
}

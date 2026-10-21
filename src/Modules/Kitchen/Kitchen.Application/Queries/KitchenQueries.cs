using Kitchen.Domain;

namespace Kitchen.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une ressource d'un
// autre tenant n'est jamais trouvée.

/// <summary>Postes d'un établissement, par nom.</summary>
public sealed record ListStationsQuery(Guid LocationId) : IQuery<IReadOnlyList<StationDto>>;

public sealed class ListStationsHandler(KitchenWorkspace workspace) : IQueryHandler<ListStationsQuery, IReadOnlyList<StationDto>>
{
    public async Task<IReadOnlyList<StationDto>> HandleAsync(ListStationsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        var stations = await workspace.Stations.ListAsync(tenant, location.LocationId, cancellationToken);
        return stations.Select(StationDto.From).ToList();
    }
}

/// <summary>Routage d'un établissement ; 404 tant qu'il n'est pas configuré.</summary>
public sealed record GetStationRoutingQuery(Guid LocationId) : IQuery<StationRoutingDto>;

public sealed class GetStationRoutingHandler(KitchenWorkspace workspace) : IQueryHandler<GetStationRoutingQuery, StationRoutingDto>
{
    public async Task<StationRoutingDto> HandleAsync(GetStationRoutingQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        return StationRoutingDto.From(await workspace.LoadRoutingAsync(tenant, location.LocationId, cancellationToken));
    }
}

public sealed record GetTicketByIdQuery(Guid TicketId) : IQuery<KitchenTicketDto>;

public sealed class GetTicketByIdHandler(KitchenWorkspace workspace) : IQueryHandler<GetTicketByIdQuery, KitchenTicketDto>
{
    public async Task<KitchenTicketDto> HandleAsync(GetTicketByIdQuery query, CancellationToken cancellationToken = default) =>
        KitchenTicketDto.From(await workspace.LoadTicketAsync(workspace.CurrentTenant, query.TicketId, cancellationToken));
}

/// <summary>
/// Écran cuisine : tickets ouverts, priorité la plus haute puis plus anciens d'abord. <paramref name="StationId"/> :
/// seulement les tickets qui ont une ligne à ce poste (l'écran d'un poste filtre ensuite ses lignes).
/// </summary>
public sealed record ListOpenTicketsQuery(Guid? LocationId = null, Guid? StationId = null) : IQuery<IReadOnlyList<KitchenTicketDto>>;

public sealed class ListOpenTicketsHandler(KitchenWorkspace workspace) : IQueryHandler<ListOpenTicketsQuery, IReadOnlyList<KitchenTicketDto>>
{
    public async Task<IReadOnlyList<KitchenTicketDto>> HandleAsync(ListOpenTicketsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        // Un id vide ne désigne rien : liste vide plutôt qu'erreur, comme un id inconnu.
        if (query.LocationId == Guid.Empty || query.StationId == Guid.Empty)
        {
            return [];
        }

        var tickets = await workspace.Tickets.ListOpenAsync(
            tenant,
            query.LocationId is { } location ? LocationId.From(location) : null,
            query.StationId is { } station ? StationId.From(station) : null,
            cancellationToken);
        return tickets.Select(KitchenTicketDto.From).ToList();
    }
}

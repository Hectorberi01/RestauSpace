using Floor.Domain;

namespace Floor.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une ressource d'un
// autre tenant n'est jamais trouvée.

public sealed record GetDiningAreaByIdQuery(Guid DiningAreaId) : IQuery<DiningAreaDto>;

public sealed class GetDiningAreaByIdHandler(FloorWorkspace workspace) : IQueryHandler<GetDiningAreaByIdQuery, DiningAreaDto>
{
    public async Task<DiningAreaDto> HandleAsync(GetDiningAreaByIdQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var area = await workspace.LoadDiningAreaAsync(tenant, query.DiningAreaId, cancellationToken);
        var open = await workspace.Sessions.ListOpenAsync(tenant, area.LocationId, cancellationToken);
        return DiningAreaDto.From(area, ByTable(open));
    }

    internal static Dictionary<TableId, TableSessionId> ByTable(IEnumerable<TableSession> openSessions)
    {
        // Une seule session ouverte par table (index unique) ; TryAdd plutôt que d'échouer la lecture si ce n'était pas le cas.
        var byTable = new Dictionary<TableId, TableSessionId>();
        foreach (var session in openSessions)
        {
            byTable.TryAdd(session.TableId, session.Id);
        }

        return byTable;
    }
}

/// <summary>
/// Plan de salle : zones par nom avec leurs tables et la session en cours de chacune. <paramref name="LocationId"/> null
/// = tous les établissements.
/// </summary>
public sealed record ListDiningAreasQuery(Guid? LocationId = null) : IQuery<IReadOnlyList<DiningAreaDto>>;

public sealed class ListDiningAreasHandler(FloorWorkspace workspace) : IQueryHandler<ListDiningAreasQuery, IReadOnlyList<DiningAreaDto>>
{
    public async Task<IReadOnlyList<DiningAreaDto>> HandleAsync(ListDiningAreasQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        // Un id vide ne désigne aucun établissement : liste vide plutôt qu'erreur, comme un id inconnu.
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var location = query.LocationId is { } id ? LocationId.From(id) : (LocationId?)null;
        var areas = await workspace.DiningAreas.ListAsync(tenant, location, cancellationToken);
        var open = GetDiningAreaByIdHandler.ByTable(await workspace.Sessions.ListOpenAsync(tenant, location, cancellationToken));
        return areas.Select(area => DiningAreaDto.From(area, open)).ToList();
    }
}

public sealed record GetTableSessionByIdQuery(Guid TableSessionId) : IQuery<TableSessionDto>;

public sealed class GetTableSessionByIdHandler(FloorWorkspace workspace) : IQueryHandler<GetTableSessionByIdQuery, TableSessionDto>
{
    public async Task<TableSessionDto> HandleAsync(GetTableSessionByIdQuery query, CancellationToken cancellationToken = default) =>
        TableSessionDto.From(await workspace.LoadSessionAsync(workspace.CurrentTenant, query.TableSessionId, cancellationToken));
}

/// <summary>Sessions ouvertes, par heure d'ouverture ; <paramref name="LocationId"/> null = tous les établissements.</summary>
public sealed record ListOpenTableSessionsQuery(Guid? LocationId = null) : IQuery<IReadOnlyList<TableSessionDto>>;

public sealed class ListOpenTableSessionsHandler(FloorWorkspace workspace)
    : IQueryHandler<ListOpenTableSessionsQuery, IReadOnlyList<TableSessionDto>>
{
    public async Task<IReadOnlyList<TableSessionDto>> HandleAsync(
        ListOpenTableSessionsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var location = query.LocationId is { } id ? LocationId.From(id) : (LocationId?)null;
        var sessions = await workspace.Sessions.ListOpenAsync(tenant, location, cancellationToken);
        return sessions.Select(TableSessionDto.From).ToList();
    }
}

using Locations.Application.Abstractions;

namespace Locations.Application.Queries;

/// <summary>Un établissement du tenant courant. Lisible quel que soit le statut du tenant ou de l'établissement.</summary>
public sealed record GetLocationByIdQuery(Guid LocationId) : IQuery<LocationDto>;

public sealed class GetLocationByIdHandler(ITenantContext tenantContext, ILocationRepository locations)
    : IQueryHandler<GetLocationByIdQuery, LocationDto>
{
    public async Task<LocationDto> HandleAsync(GetLocationByIdQuery query, CancellationToken cancellationToken = default)
    {
        var location = await LocationEditor.FindAsync(tenantContext.RequireTenant(), query.LocationId, locations, cancellationToken);
        return LocationDto.From(location);
    }
}

/// <summary>Établissements du tenant courant, par nom. Les archivés seulement sur demande.</summary>
public sealed record ListLocationsQuery(int Page = 1, int PageSize = ListLocationsQuery.DefaultPageSize, bool IncludeArchived = false)
    : IQuery<PagedResult<LocationSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Garde l'offset loin du dépassement de capacité.</summary>
    public const int MaxPage = 100_000;
}

public sealed class ListLocationsHandler(ITenantContext tenantContext, ILocationRepository locations)
    : IQueryHandler<ListLocationsQuery, PagedResult<LocationSummaryDto>>
{
    public async Task<PagedResult<LocationSummaryDto>> HandleAsync(ListLocationsQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page is < 1 or > ListLocationsQuery.MaxPage || query.PageSize is < 1 or > ListLocationsQuery.MaxPageSize)
        {
            throw new DomainException(LocationsApplicationErrors.PagingInvalid);
        }

        var page = await locations.ListAsync(
            tenantContext.RequireTenant(),
            query.IncludeArchived,
            (query.Page - 1) * query.PageSize,
            query.PageSize,
            cancellationToken);
        return PagedResult<LocationSummaryDto>.Counted(
            page.Items.Select(LocationSummaryDto.From).ToList(), query.Page, query.PageSize, page.TotalCount);
    }
}

/// <summary>
/// L'établissement du tenant courant est-il ouvert pendant toute la plage donnée (instants UTC, lus à son heure
/// locale) ? Faux s'il n'est pas opérationnel. Pour les autres modules (réservations), via l'hôte.
/// </summary>
public sealed record IsLocationOpenDuringQuery(Guid LocationId, DateTimeOffset StartUtc, DateTimeOffset EndUtc) : IQuery<bool>;

public sealed class IsLocationOpenDuringHandler(ITenantContext tenantContext, ILocationRepository locations)
    : IQueryHandler<IsLocationOpenDuringQuery, bool>
{
    public async Task<bool> HandleAsync(IsLocationOpenDuringQuery query, CancellationToken cancellationToken = default)
    {
        var location = await LocationEditor.FindAsync(tenantContext.RequireTenant(), query.LocationId, locations, cancellationToken);
        return location.IsOpenDuring(Period.Create(query.StartUtc, query.EndUtc));
    }
}

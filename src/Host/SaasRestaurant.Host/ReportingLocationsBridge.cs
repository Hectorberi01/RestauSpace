using Reporting.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

/// <summary>Reporting -> Locations : le tenant du contexte a-t-il plus d'un établissement non archivé ? Faux pour un autre tenant.</summary>
internal sealed class ReportingLocationsFromLocations(
    ITenantContext tenantContext,
    IQueryHandler<Locations.Application.Queries.ListLocationsQuery, PagedResult<Locations.Application.Queries.LocationSummaryDto>> locations)
    : IReportingLocations
{
    public async Task<bool> HasSeveralAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        var page = await locations.HandleAsync(new Locations.Application.Queries.ListLocationsQuery(1, 2), cancellationToken);
        return page.Items.Count > 1 || page.HasMore;
    }
}

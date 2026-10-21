using Tenants.Application.Abstractions;

namespace Tenants.Application.Queries;

/// <summary>
/// Le restaurant sélectionné par le membre connecté. Le tenant vient UNIQUEMENT du contexte authentifié (claim
/// <c>tid</c>) : aucun identifiant n'est accepté de la requête.
/// </summary>
public sealed record GetCurrentTenantQuery : IQuery<TenantDto>;

public sealed class GetCurrentTenantHandler(ITenantContext tenantContext, ITenantRepository tenants)
    : IQueryHandler<GetCurrentTenantQuery, TenantDto>
{
    public async Task<TenantDto> HandleAsync(GetCurrentTenantQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireTenant();
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        return TenantDto.From(tenant);
    }
}

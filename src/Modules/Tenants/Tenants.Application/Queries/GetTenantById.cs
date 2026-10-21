using Tenants.Application.Abstractions;

namespace Tenants.Application.Queries;

/// <summary>Lit un tenant quel que soit son statut : les tenants suspendus et fermés restent lisibles.</summary>
public sealed record GetTenantByIdQuery(Guid TenantId) : IQuery<TenantDto>;

public sealed class GetTenantByIdHandler(ITenantRepository tenants) : IQueryHandler<GetTenantByIdQuery, TenantDto>
{
    public async Task<TenantDto> HandleAsync(GetTenantByIdQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = await tenants.FindAsync(TenantId.From(query.TenantId), cancellationToken);
        if (tenant == null)
        {
            throw new NotFoundException(TenantApplicationErrors.NotFound);
        }
        return TenantDto.From(tenant);
    }
}
